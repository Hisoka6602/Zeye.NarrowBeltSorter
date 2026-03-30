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
    /// Leadshaine IoPanel 实现（消费 EMC 快照，执行按钮边沿检测并发布事件）。
    /// </summary>
    public sealed class LeadshaineIoPanel : IIoPanel, IAsyncDisposable {
        private readonly object _stateLock = new();
        private readonly ILogger<LeadshaineIoPanel> _logger;
        private readonly SafeExecutor _executor;
        private readonly IEmcController _emc;
        private readonly LeadshaineIoPanelButtonBindingCollectionOptions _buttonOptions;
        private readonly LeadshainePointBindingCollectionOptions _pointOptions;
        private readonly LeadshaineEmcConnectionOptions _connectionOptions;
        private readonly Dictionary<string, IoState> _buttonStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _buttonNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IoPanelButtonType> _buttonTypes = new(StringComparer.OrdinalIgnoreCase);
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
        public event EventHandler<IoPanelButtonStateChangedEventArgs>? ButtonStateChanged;

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
            _logger.LogInformation("Leadshaine IoPanel 监控已启动，按钮数量={ButtonCount}。", _buttonStates.Count);
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
        /// 构建按钮点位映射。
        /// </summary>
        private void BuildButtonMappings() {
            // 步骤1：锁外完成点位查找与区域校验，仅将合法条目写入字典，减少锁占用时间。
            var pointMap = _pointOptions.Points
                .Where(x => !string.IsNullOrWhiteSpace(x.PointId))
                .ToDictionary(x => x.PointId, x => x, StringComparer.OrdinalIgnoreCase);
            List<string> pendingFaultMessages = [];
            List<(string PointId, string ButtonName, IoPanelButtonType ButtonType)> validButtons = [];

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

                validButtons.Add((button.PointId, button.ButtonName, button.ButtonType));
            }

            // 步骤2：锁内统一写入合法条目，缩短持锁区间。
            lock (_stateLock) {
                _buttonStates.Clear();
                _buttonNames.Clear();
                _buttonTypes.Clear();

                foreach (var (pointId, buttonName, buttonType) in validButtons) {
                    _buttonStates[pointId] = IoState.Low;
                    _buttonNames[pointId] = buttonName;
                    _buttonTypes[pointId] = buttonType;
                }
            }

            // 步骤3：锁外发布配置异常，避免占锁触发外部回调。
            foreach (var message in pendingFaultMessages) {
                PublishFault(message, null);
            }
        }

        /// <summary>
        /// 监控循环：执行按钮边沿检测并发布变更事件。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>监控任务。</returns>
        private async Task MonitoringLoopAsync(CancellationToken cancellationToken) {
            while (!cancellationToken.IsCancellationRequested) {
                var now = DateTime.Now;
                List<IoPanelButtonStateChangedEventArgs> changedEvents = [];

                lock (_stateLock) {
                    // 步骤1：遍历按钮，查询 EMC 快照并收集边沿变化。
                    List<(string PointId, IoState NewState)> pendingUpdates = [];
                    foreach (var buttonState in _buttonStates) {
                        if (!_emc.TryGetMonitoredPoint(buttonState.Key, out var pointInfo)) {
                            continue;
                        }

                        var newState = pointInfo.Value ? IoState.High : IoState.Low;
                        if (buttonState.Value == newState) {
                            continue;
                        }

                        pendingUpdates.Add((buttonState.Key, newState));
                        changedEvents.Add(new IoPanelButtonStateChangedEventArgs(
                            buttonState.Key,
                            _buttonNames[buttonState.Key],
                            _buttonTypes[buttonState.Key],  // BuildButtonMappings 保证三个字典同步写入，key 必定存在
                            buttonState.Value,
                            newState,
                            now));
                    }

                    // 步骤2：循环结束后统一写回状态，避免在 foreach 内修改字典引发异常。
                    foreach (var (pointId, newState) in pendingUpdates) {
                        _buttonStates[pointId] = newState;
                    }
                }

                // 步骤3：在锁外发布按钮变更事件并记录日志，避免阻塞监控采样。
                foreach (var changedEvent in changedEvents) {
                    _ = _executor.Execute(() => {
                        _logger.LogInformation(
                            "IoPanel 按钮状态变化 button={ButtonName} buttonType={ButtonType} pointId={PointId} oldState={OldState} newState={NewState}",
                            changedEvent.ButtonName,
                            changedEvent.ButtonType,
                            changedEvent.PointId,
                            changedEvent.OldState,
                            changedEvent.NewState);
                        ButtonStateChanged?.Invoke(this, changedEvent);
                    }, "LeadshaineIoPanel.ButtonStateChanged");
                }

                // 步骤4：按 EMC 轮询间隔等待下一轮采样。
                await Task.Delay(_connectionOptions.PollingIntervalMs, cancellationToken).ConfigureAwait(false);
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
        /// 在对象释放后抛出异常。
        /// </summary>
        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(LeadshaineIoPanel));
            }
        }
    }
}
