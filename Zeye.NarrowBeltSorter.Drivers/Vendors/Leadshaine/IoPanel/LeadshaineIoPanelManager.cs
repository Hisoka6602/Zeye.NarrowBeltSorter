using Microsoft.Extensions.Logging;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Manager.Emc;
using Zeye.NarrowBeltSorter.Core.Options.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.IoPanel {
    /// <summary>
    /// Leadshaine IoPanel 管理器（消费 EMC 快照并执行按钮边沿检测）。
    /// </summary>
    public sealed class LeadshaineIoPanelManager : IAsyncDisposable {
        private readonly object _stateLock = new();
        private readonly ILogger<LeadshaineIoPanelManager> _logger;
        private readonly SafeExecutor _executor;
        private readonly IEmcController _emcController;
        private readonly LeadshaineIoPanelButtonBindingCollectionOptions _buttonOptions;
        private readonly LeadshainePointBindingCollectionOptions _pointOptions;
        private readonly LeadshaineEmcConnectionOptions _connectionOptions;
        private readonly Dictionary<string, IoState> _buttonStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _buttonNames = new(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource? _monitoringCts;
        private Task? _monitoringTask;
        private bool _disposed;

        /// <summary>
        /// 初始化 Leadshaine IoPanel 管理器。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <param name="emcController">EMC 控制器。</param>
        /// <param name="buttonOptions">按钮绑定配置。</param>
        /// <param name="pointOptions">点位绑定配置。</param>
        /// <param name="connectionOptions">EMC 连接配置。</param>
        public LeadshaineIoPanelManager(
            ILogger<LeadshaineIoPanelManager> logger,
            SafeExecutor safeExecutor,
            IEmcController emcController,
            LeadshaineIoPanelButtonBindingCollectionOptions buttonOptions,
            LeadshainePointBindingCollectionOptions pointOptions,
            LeadshaineEmcConnectionOptions connectionOptions) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _executor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _emcController = emcController ?? throw new ArgumentNullException(nameof(emcController));
            _buttonOptions = buttonOptions ?? throw new ArgumentNullException(nameof(buttonOptions));
            _pointOptions = pointOptions ?? throw new ArgumentNullException(nameof(pointOptions));
            _connectionOptions = connectionOptions ?? throw new ArgumentNullException(nameof(connectionOptions));
        }

        /// <summary>
        /// 启动 IoPanel 监控。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        public ValueTask StartMonitoringAsync(CancellationToken cancellationToken = default) {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (_monitoringTask is not null && !_monitoringTask.IsCompleted) {
                return ValueTask.CompletedTask;
            }

            // 步骤1：根据配置初始化按钮点位映射。
            BuildButtonMappings();

            // 步骤2：启动监控循环。
            _monitoringCts = new CancellationTokenSource();
            _monitoringTask = Task.Run(() => MonitoringLoopAsync(_monitoringCts.Token), _monitoringCts.Token);
            _logger.LogInformation("Leadshaine IoPanel 监控已启动，按钮数量={ButtonCount}。", _buttonStates.Count);
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// 停止 IoPanel 监控。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        public async ValueTask StopMonitoringAsync(CancellationToken cancellationToken = default) {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (_monitoringCts is null) {
                return;
            }

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
            _logger.LogInformation("Leadshaine IoPanel 监控已停止。");
        }

        /// <summary>
        /// 释放管理器资源。
        /// </summary>
        public async ValueTask DisposeAsync() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            await StopMonitoringAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 构建按钮点位映射。
        /// </summary>
        private void BuildButtonMappings() {
            var pointMap = _pointOptions.Points
                .Where(x => !string.IsNullOrWhiteSpace(x.PointId))
                .ToDictionary(x => x.PointId, x => x, StringComparer.OrdinalIgnoreCase);

            lock (_stateLock) {
                _buttonStates.Clear();
                _buttonNames.Clear();

                foreach (var button in _buttonOptions.Buttons) {
                    if (string.IsNullOrWhiteSpace(button.PointId)) {
                        continue;
                    }

                    if (!pointMap.TryGetValue(button.PointId, out var point)) {
                        _logger.LogWarning("IoPanel 按钮点位未找到，跳过 pointId={PointId}", button.PointId);
                        continue;
                    }

                    if (!string.Equals(point.Binding.Area, "Input", StringComparison.OrdinalIgnoreCase)) {
                        _logger.LogWarning("IoPanel 按钮点位必须为输入区，跳过 pointId={PointId}", button.PointId);
                        continue;
                    }

                    _buttonStates[button.PointId] = IoState.Low;
                    _buttonNames[button.PointId] = button.ButtonName;
                }
            }
        }

        /// <summary>
        /// 监控循环：执行按钮边沿检测。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>监控任务。</returns>
        private async Task MonitoringLoopAsync(CancellationToken cancellationToken) {
            while (!cancellationToken.IsCancellationRequested) {
                var points = _emcController.MonitoredIoPoints
                    .ToDictionary(x => x.PointId, x => x, StringComparer.OrdinalIgnoreCase);

                List<(string PointId, IoState OldState, IoState NewState)> edges = [];
                lock (_stateLock) {
                    foreach (var buttonState in _buttonStates.ToArray()) {
                        if (!points.TryGetValue(buttonState.Key, out var pointInfo)) {
                            continue;
                        }

                        var newState = pointInfo.Value ? IoState.High : IoState.Low;
                        if (buttonState.Value == newState) {
                            continue;
                        }

                        _buttonStates[buttonState.Key] = newState;
                        edges.Add((buttonState.Key, buttonState.Value, newState));
                    }
                }

                foreach (var edge in edges) {
                    // 步骤1：记录按钮边沿事件日志。
                    _ = _executor.Execute(() => _logger.LogInformation(
                            "IoPanel 按钮状态变化 button={ButtonName} pointId={PointId} oldState={OldState} newState={NewState}",
                            _buttonNames[edge.PointId],
                            edge.PointId,
                            edge.OldState,
                            edge.NewState),
                        "LeadshaineIoPanelManager.ButtonEdgeLog");
                }

                // 步骤2：按 EMC 轮询间隔等待下一轮采样。
                await Task.Delay(_connectionOptions.PollingIntervalMs, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 在对象释放后抛出异常。
        /// </summary>
        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(LeadshaineIoPanelManager));
            }
        }
    }
}
