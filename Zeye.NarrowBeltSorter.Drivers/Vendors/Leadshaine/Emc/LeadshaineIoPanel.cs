using Microsoft.Extensions.Logging;
using Zeye.NarrowBeltSorter.Core.Enums.Emc;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Events.Emc;
using Zeye.NarrowBeltSorter.Core.Events.IoPanel;
using Zeye.NarrowBeltSorter.Core.Manager.Emc;
using Zeye.NarrowBeltSorter.Core.Manager.IoPanel;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc.Options;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc {
    /// <summary>
    /// Leadshaine IoPanel 实现（消费 EMC 快照，按 TriggerState 方向检测按下/释放边沿并按角色发布事件）。
    /// </summary>
    public sealed class LeadshaineIoPanel : IIoPanel, IAsyncDisposable {
        private readonly object _stateLock = new();
        private readonly ILogger<LeadshaineIoPanel> _logger;
        private readonly SafeExecutor _executor;
        private readonly IEmcController _emc;
        private readonly LeadshaineIoPanelButtonBindingCollectionOptions _buttonOptions;
        private readonly LeadshainePointBindingCollectionOptions _pointOptions;
        private readonly LeadshaineEmcConnectionOptions _connectionOptions;
        private readonly Dictionary<string, string> _buttonNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IoPanelButtonType> _buttonTypes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IoState> _buttonTriggerStates = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// 上一轮采样电平；null 表示首次采样尚未完成，避免启动瞬间误触发边沿事件。
        /// </summary>
        private readonly Dictionary<string, IoState?> _buttonLastStates = new(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource? _monitoringCts;
        private Task? _monitoringTask;
        private bool _disposed;

        /// <summary>
        /// 初始化 Leadshaine IoPanel。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <param name="emcController">EMC 控制器。</param>
        /// <param name="buttonOptions">按钮绑定配置。</param>
        /// <param name="pointOptions">点位绑定配置。</param>
        /// <param name="connectionOptions">EMC 连接配置。</param>
        public LeadshaineIoPanel(
            ILogger<LeadshaineIoPanel> logger,
            SafeExecutor safeExecutor,
            IEmcController emcController,
            LeadshaineIoPanelButtonBindingCollectionOptions buttonOptions,
            LeadshainePointBindingCollectionOptions pointOptions,
            LeadshaineEmcConnectionOptions connectionOptions) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _executor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _emc = emcController ?? throw new ArgumentNullException(nameof(emcController));
            _buttonOptions = buttonOptions ?? throw new ArgumentNullException(nameof(buttonOptions));
            _pointOptions = pointOptions ?? throw new ArgumentNullException(nameof(pointOptions));
            _connectionOptions = connectionOptions ?? throw new ArgumentNullException(nameof(connectionOptions));
            _emc.StatusChanged += HandleEmcStatusChanged;
            Status = IoPanelMonitoringStatus.Stopped;
        }

        /// <inheritdoc />
        public IoPanelMonitoringStatus Status { get; private set; }

        /// <inheritdoc />
        public bool IsMonitoring => Status == IoPanelMonitoringStatus.Monitoring;

        /// <inheritdoc />
        public event EventHandler<IoPanelButtonPressedEventArgs>? StartButtonPressed;

        /// <inheritdoc />
        public event EventHandler<IoPanelButtonPressedEventArgs>? StopButtonPressed;

        /// <inheritdoc />
        public event EventHandler<IoPanelButtonPressedEventArgs>? EmergencyStopButtonPressed;

        /// <inheritdoc />
        public event EventHandler<IoPanelButtonPressedEventArgs>? ResetButtonPressed;

        /// <inheritdoc />
        public event EventHandler<IoPanelButtonReleasedEventArgs>? EmergencyStopButtonReleased;

        /// <inheritdoc />
        public event EventHandler<IoPanelMonitoringStatusChangedEventArgs>? MonitoringStatusChanged;

        /// <inheritdoc />
        public event EventHandler<IoPanelFaultedEventArgs>? Faulted;

        /// <inheritdoc />
        public ValueTask StartMonitoringAsync(CancellationToken cancellationToken = default) {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (IsMonitoring) {
                return ValueTask.CompletedTask;
            }

            // 步骤1：根据配置初始化按钮点位映射。
            BuildButtonMappings();

            // 步骤2：启动监控循环并切换状态。
            _monitoringCts = new CancellationTokenSource();
            _monitoringTask = Task.Run(() => MonitoringLoopAsync(_monitoringCts.Token), _monitoringCts.Token);
            SetStatus(IoPanelMonitoringStatus.Monitoring);
            _logger.LogInformation("Leadshaine IoPanel 监控已启动，按钮数量={ButtonCount}。", _buttonNames.Count);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public async ValueTask StopMonitoringAsync(CancellationToken cancellationToken = default) {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsMonitoring) {
                return;
            }

            // 步骤1：发送停止信号并等待监控循环退出。
            if (_monitoringCts is not null) {
                _monitoringCts.Cancel();
                if (_monitoringTask is not null) {
                    try {
                        await _monitoringTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) {
                        // 取消停止属于预期路径，不做额外处理。
                    }
                }

                _monitoringCts.Dispose();
                _monitoringCts = null;
                _monitoringTask = null;
            }

            // 步骤2：切换状态并记录日志。
            SetStatus(IoPanelMonitoringStatus.Stopped);
            _logger.LogInformation("Leadshaine IoPanel 监控已停止。");
        }

        /// <summary>
        /// 释放 IoPanel 资源。
        /// </summary>
        public async ValueTask DisposeAsync() {
            if (_disposed) {
                return;
            }

            _emc.StatusChanged -= HandleEmcStatusChanged;
            _disposed = true;
            await StopMonitoringAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 构建按钮点位映射（含 TriggerState 解析）。
        /// </summary>
        private void BuildButtonMappings() {
            // 步骤1：锁外完成点位查找与区域校验，仅将合法条目写入字典，减少锁占用时间。
            var pointMap = _pointOptions.Points
                .Where(x => !string.IsNullOrWhiteSpace(x.PointId))
                .ToDictionary(x => x.PointId, x => x, StringComparer.OrdinalIgnoreCase);
            List<string> pendingFaultMessages = [];
            List<(string PointId, string ButtonName, IoPanelButtonType ButtonType, IoState TriggerState)> validButtons = [];

            foreach (var button in _buttonOptions.Buttons) {
                if (string.IsNullOrWhiteSpace(button.PointId)) {
                    continue;
                }

                if (!pointMap.TryGetValue(button.PointId, out var point)) {
                    pendingFaultMessages.Add($"IoPanel 按钮点位未找到: PointId={button.PointId}。");
                    continue;
                }

                if (!string.Equals(point.Binding.Area, "Input", StringComparison.OrdinalIgnoreCase)) {
                    pendingFaultMessages.Add($"IoPanel 按钮点位必须为输入区: PointId={button.PointId}。");
                    continue;
                }

                var triggerState = ParseTriggerState(point.Binding.TriggerState);
                validButtons.Add((button.PointId, button.ButtonName, button.ButtonType, triggerState));
            }

            // 步骤2：锁内统一写入合法条目，并重置首次采样标记，缩短持锁区间。
            lock (_stateLock) {
                _buttonNames.Clear();
                _buttonTypes.Clear();
                _buttonTriggerStates.Clear();
                _buttonLastStates.Clear();

                foreach (var (pointId, buttonName, buttonType, triggerState) in validButtons) {
                    _buttonNames[pointId] = buttonName;
                    _buttonTypes[pointId] = buttonType;
                    _buttonTriggerStates[pointId] = triggerState;
                    _buttonLastStates[pointId] = null;  // null 表示首次采样未完成
                }
            }

            // 步骤3：锁外发布配置异常，避免占锁触发外部回调。
            foreach (var message in pendingFaultMessages) {
                PublishFault(message, null);
            }
        }

        /// <summary>
        /// 监控循环：按 TriggerState 方向检测按下/释放边沿并按角色发布事件。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>监控任务。</returns>
        private async Task MonitoringLoopAsync(CancellationToken cancellationToken) {
            while (!cancellationToken.IsCancellationRequested) {
                var now = DateTime.Now;
                List<IoPanelButtonPressedEventArgs> pressedEvents = [];
                List<IoPanelButtonReleasedEventArgs> releasedEvents = [];

                lock (_stateLock) {
                    // 步骤1：收集本轮状态更新与边沿事件。
                    List<(string PointId, IoState CurrentState)> pendingUpdates = [];
                    foreach (var pointId in _buttonTriggerStates.Keys) {
                        if (!_emc.TryGetMonitoredPoint(pointId, out var pointInfo)) {
                            continue;
                        }

                        var currentState = pointInfo.Value ? IoState.High : IoState.Low;
                        var lastState = _buttonLastStates[pointId];  // 四个字典同步写入，key 必定存在
                        pendingUpdates.Add((pointId, currentState));

                        // 首次采样仅记录，避免启动瞬间误触发边沿事件。
                        if (lastState is null) {
                            continue;
                        }

                        var triggerState = _buttonTriggerStates[pointId];
                        var isPressedEdge = lastState.Value != triggerState && currentState == triggerState;
                        var isReleasedEdge = lastState.Value == triggerState && currentState != triggerState;

                        if (isPressedEdge) {
                            pressedEvents.Add(new IoPanelButtonPressedEventArgs(
                                pointId,
                                _buttonNames[pointId],
                                _buttonTypes[pointId],
                                now));
                        } else if (isReleasedEdge && _buttonTypes[pointId] == IoPanelButtonType.EmergencyStop) {
                            releasedEvents.Add(new IoPanelButtonReleasedEventArgs(
                                pointId,
                                _buttonNames[pointId],
                                IoPanelButtonType.EmergencyStop,
                                now));
                        }
                    }

                    // 步骤2：循环结束后统一写回采样状态，避免在 foreach 内修改字典引发异常。
                    foreach (var (pointId, currentState) in pendingUpdates) {
                        _buttonLastStates[pointId] = currentState;
                    }
                }

                // 步骤3：在锁外发布按下事件并记录日志，避免阻塞监控采样。
                foreach (var pressedEvent in pressedEvents) {
                    _ = _executor.Execute(() => {
                        _logger.LogInformation(
                            "IoPanel 按钮按下 button={ButtonName} buttonType={ButtonType} pointId={PointId}",
                            pressedEvent.ButtonName,
                            pressedEvent.ButtonType,
                            pressedEvent.PointId);
                        FirePressedEvent(pressedEvent);
                    }, "LeadshaineIoPanel.ButtonPressed");
                }

                // 步骤4：在锁外发布急停释放事件并记录日志。
                foreach (var releasedEvent in releasedEvents) {
                    _ = _executor.Execute(() => {
                        _logger.LogInformation(
                            "IoPanel 急停按钮释放 button={ButtonName} pointId={PointId}",
                            releasedEvent.ButtonName,
                            releasedEvent.PointId);
                        EmergencyStopButtonReleased?.Invoke(this, releasedEvent);
                    }, "LeadshaineIoPanel.EmergencyStopReleased");
                }

                // 步骤5：按 EMC 轮询间隔等待下一轮采样。
                await Task.Delay(_connectionOptions.PollingIntervalMs, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 根据按钮角色类型将按下事件路由到对应的角色事件。
        /// </summary>
        /// <param name="args">按下事件载荷。</param>
        private void FirePressedEvent(IoPanelButtonPressedEventArgs args) {
            switch (args.ButtonType) {
                case IoPanelButtonType.Start:
                    StartButtonPressed?.Invoke(this, args);
                    break;
                case IoPanelButtonType.Stop:
                    StopButtonPressed?.Invoke(this, args);
                    break;
                case IoPanelButtonType.EmergencyStop:
                    EmergencyStopButtonPressed?.Invoke(this, args);
                    break;
                case IoPanelButtonType.Reset:
                    ResetButtonPressed?.Invoke(this, args);
                    break;
                default:
                    _logger.LogInformation("IoPanel 收到未处理的按钮类型：{ButtonType}，pointId={PointId}",
                        args.ButtonType, args.PointId);
                    break;
            }
        }

        /// <summary>
        /// 处理 EMC 状态变化事件。
        /// </summary>
        /// <param name="sender">事件发送方。</param>
        /// <param name="args">状态变化参数。</param>
        private void HandleEmcStatusChanged(object? sender, EmcStatusChangedEventArgs args) {
            if (args.NewStatus is not (EmcControllerStatus.Disconnected or EmcControllerStatus.Faulted)) {
                return;
            }

            PublishFault($"EMC 状态异常：{args.NewStatus}。", null);
        }

        /// <summary>
        /// 发布 IoPanel 故障事件并记录日志。
        /// </summary>
        /// <param name="message">故障消息。</param>
        /// <param name="exception">异常对象。</param>
        private void PublishFault(string message, Exception? exception) {
            SetStatus(IoPanelMonitoringStatus.Faulted);
            _logger.LogError(exception, "Leadshaine IoPanel 异常：{Message}", message);
            _ = _executor.Execute(
                () => Faulted?.Invoke(this, new IoPanelFaultedEventArgs(message, exception, DateTime.Now)),
                "LeadshaineIoPanel.Faulted");
        }

        /// <summary>
        /// 切换 IoPanel 监控状态并发布状态变更事件。
        /// </summary>
        /// <param name="newStatus">新状态。</param>
        private void SetStatus(IoPanelMonitoringStatus newStatus) {
            var oldStatus = Status;
            if (oldStatus == newStatus) {
                return;
            }

            Status = newStatus;
            _ = _executor.Execute(
                () => MonitoringStatusChanged?.Invoke(
                    this,
                    new IoPanelMonitoringStatusChangedEventArgs(oldStatus, newStatus, DateTime.Now)),
                "LeadshaineIoPanel.MonitoringStatusChanged");
        }

        /// <summary>
        /// 解析触发电平配置字符串。
        /// </summary>
        /// <param name="triggerState">触发电平配置（"High" 或 "Low"）。</param>
        /// <returns>触发电平枚举值。</returns>
        private static IoState ParseTriggerState(string triggerState) {
            return string.Equals(triggerState, "Low", StringComparison.OrdinalIgnoreCase)
                ? IoState.Low
                : IoState.High;
        }

        /// <summary>
        /// 在对象释放后抛出异常。
        /// </summary>
        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(LeadshaineIoPanel));
            }
        }
    }
}
