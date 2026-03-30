using Microsoft.Extensions.Logging;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.Io;
using Zeye.NarrowBeltSorter.Core.Manager.Emc;
using Zeye.NarrowBeltSorter.Core.Manager.Sensor;
using Zeye.NarrowBeltSorter.Core.Models.Sensor;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc.Options;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Sensor {
    /// <summary>
    /// Leadshaine 传感器管理器（消费 EMC 快照并发布传感器事件）。
    /// </summary>
    public sealed class LeadshaineSensorManager : ISensorManager, IAsyncDisposable {
        private const int CardPointFactor = 100000;
        private const int PortPointFactor = 100;
        private readonly object _stateLock = new();
        private readonly ILogger<LeadshaineSensorManager> _logger;
        private readonly SafeExecutor _executor;
        private readonly IEmcController _emc;
        private readonly LeadshaineSensorBindingCollectionOptions _sensorOptions;
        private readonly LeadshainePointBindingCollectionOptions _pointOptions;
        private readonly LeadshaineEmcConnectionOptions _connectionOptions;
        private readonly Dictionary<string, SensorInfo> _sensorInfos = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _sensorNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IoState> _triggerStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _debounceWindowMs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _pollIntervalMs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _lastPolledTickMsByPoint = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _lastPublishedAtByPoint = new(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource? _monitoringCts;
        private Task? _monitoringTask;
        private bool _disposed;

        /// <summary>
        /// 初始化 Leadshaine 传感器管理器。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <param name="emcController">EMC 控制器。</param>
        /// <param name="sensorOptions">传感器绑定配置。</param>
        /// <param name="pointOptions">点位绑定配置。</param>
        /// <param name="connectionOptions">EMC 连接配置。</param>
        public LeadshaineSensorManager(
            ILogger<LeadshaineSensorManager> logger,
            SafeExecutor safeExecutor,
            IEmcController emcController,
            LeadshaineSensorBindingCollectionOptions sensorOptions,
            LeadshainePointBindingCollectionOptions pointOptions,
            LeadshaineEmcConnectionOptions connectionOptions) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _executor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _emc = emcController ?? throw new ArgumentNullException(nameof(emcController));
            _sensorOptions = sensorOptions ?? throw new ArgumentNullException(nameof(sensorOptions));
            _pointOptions = pointOptions ?? throw new ArgumentNullException(nameof(pointOptions));
            _connectionOptions = connectionOptions ?? throw new ArgumentNullException(nameof(connectionOptions));
            _emc.StatusChanged += HandleEmcStatusChanged;
            Status = SensorMonitoringStatus.Stopped;
        }

        /// <inheritdoc />
        public SensorMonitoringStatus Status { get; private set; }

        /// <inheritdoc />
        public bool IsMonitoring => Status == SensorMonitoringStatus.Monitoring;

        /// <inheritdoc />
        public IReadOnlyList<SensorInfo> Sensors {
            get {
                lock (_stateLock) {
                    return _sensorInfos.Values.ToList();
                }
            }
        }

        /// <inheritdoc />
        public event EventHandler<SensorStateChangedEventArgs>? SensorStateChanged;

        /// <inheritdoc />
        public event EventHandler<SensorMonitoringStatusChangedEventArgs>? MonitoringStatusChanged;

        /// <inheritdoc />
        public event EventHandler<SensorFaultedEventArgs>? Faulted;

        /// <inheritdoc />
        public async ValueTask StartMonitoringAsync(CancellationToken cancellationToken = default) {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (IsMonitoring) {
                return;
            }

            // 步骤1：基于配置构建传感器监控映射。
            BuildSensorMappings();

            // 步骤1补充：将当前传感器点位同步至 EMC 监控列表。
            var synchronized = await SensorWorkflowHelper.SyncMonitoredIoPointsToEmcAsync(
                _emc,
                _sensorInfos.Keys.ToArray(),
                cancellationToken).ConfigureAwait(false);
            if (!synchronized) {
                PublishFault("同步传感器点位到 EMC 失败。", null);
                return;
            }

            // 步骤2：启动监控循环并切换状态。
            _monitoringCts = new CancellationTokenSource();
            _monitoringTask = Task.Run(() => MonitoringLoopAsync(_monitoringCts.Token), _monitoringCts.Token);
            SetStatus(SensorMonitoringStatus.Monitoring);
            _logger.LogInformation("Leadshaine 传感器监控已启动，传感器数量={SensorCount}。", _sensorInfos.Count);
            return;
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
            SetStatus(SensorMonitoringStatus.Stopped);
            _logger.LogInformation("Leadshaine 传感器监控已停止。");
        }

        /// <summary>
        /// 释放管理器资源。
        /// </summary>
        public async ValueTask DisposeAsync() {
            if (_disposed) {
                return;
            }

            _emc.StatusChanged -= HandleEmcStatusChanged;
            await StopMonitoringAsync().ConfigureAwait(false);
            _disposed = true;
        }

        /// <summary>
        /// 构建传感器监控映射表。
        /// </summary>
        private void BuildSensorMappings() {
            // 步骤1：读取 PointBindings 快照并构建查询索引。
            var pointMap = _pointOptions.Points
                .Where(x => !string.IsNullOrWhiteSpace(x.PointId))
                .ToDictionary(x => x.PointId, x => x, StringComparer.OrdinalIgnoreCase);
            List<string> pendingFaultMessages = [];

            // 步骤2：遍历传感器配置并生成运行时映射。
            lock (_stateLock) {
                _sensorInfos.Clear();
                _sensorNames.Clear();
                _triggerStates.Clear();
                _debounceWindowMs.Clear();
                _pollIntervalMs.Clear();
                _lastPolledTickMsByPoint.Clear();
                _lastPublishedAtByPoint.Clear();

                foreach (var sensor in _sensorOptions.Sensors) {
                    if (string.IsNullOrWhiteSpace(sensor.PointId)) {
                        continue;
                    }

                    if (!pointMap.TryGetValue(sensor.PointId, out var point)) {
                        pendingFaultMessages.Add($"传感器点位未找到: PointId={sensor.PointId}。");
                        continue;
                    }

                    if (!string.Equals(point.Binding.Area, "Input", StringComparison.OrdinalIgnoreCase)) {
                        pendingFaultMessages.Add($"传感器点位必须为输入区: PointId={sensor.PointId}。");
                        continue;
                    }

                    var sensorType = sensor.ResolveSensorType();
                    _sensorInfos[sensor.PointId] = new SensorInfo {
                        Point = BuildSensorPointNumber(point.Binding.CardNo, point.Binding.PortNo, point.Binding.BitIndex),
                        Type = sensorType,
                        State = IoState.Low
                    };
                    _sensorNames[sensor.PointId] = sensor.SensorName;
                    _triggerStates[sensor.PointId] = ParseTriggerState(point.Binding.TriggerState);
                    _debounceWindowMs[sensor.PointId] = Math.Max(0, sensor.DebounceWindowMs);
                    _pollIntervalMs[sensor.PointId] = sensor.ResolvePollIntervalMs(_connectionOptions.PollingIntervalMs);
                }
            }

            // 步骤3：锁外发布配置异常，避免占锁触发外部回调。
            foreach (var message in pendingFaultMessages) {
                PublishFault(message, null);
            }
        }

        /// <summary>
        /// 执行传感器监控循环。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>监控任务。</returns>
        private async Task MonitoringLoopAsync(CancellationToken cancellationToken) {
            while (!cancellationToken.IsCancellationRequested) {
                // 步骤1：对比 EMC 快照，在锁内按点位查询，避免全量克隆字典。
                var now = DateTime.Now;
                var nowTickMs = Environment.TickCount64;
                var occurredAtLocalMs = now.Ticks / TimeSpan.TicksPerMillisecond;
                List<SensorStateChangedEventArgs> changedEvents = [];
                List<(string PointId, SensorInfo NewInfo, DateTime PublishedAt)> pendingUpdates = [];
                var nextDelayMs = _connectionOptions.PollingIntervalMs;

                lock (_stateLock) {
                    foreach (var sensorPointId in _sensorInfos.Keys) {
                        var pollIntervalMs = _pollIntervalMs[sensorPointId];
                        if (_lastPolledTickMsByPoint.TryGetValue(sensorPointId, out var lastPolledTickMs)) {
                            var elapsedMs = nowTickMs - lastPolledTickMs;
                            if (elapsedMs < pollIntervalMs) {
                                nextDelayMs = Math.Min(nextDelayMs, pollIntervalMs - (int)elapsedMs);
                                continue;
                            }
                        }
                        nextDelayMs = Math.Min(nextDelayMs, pollIntervalMs);
                        if (!_emc.TryGetMonitoredPoint(sensorPointId, out var pointInfo)) {
                            continue;
                        }
                        _lastPolledTickMsByPoint[sensorPointId] = nowTickMs;

                        var sensorInfo = _sensorInfos[sensorPointId];
                        var newState = pointInfo.Value ? IoState.High : IoState.Low;
                        if (sensorInfo.State == newState) {
                            continue;
                        }

                        var lastPublishedAt = _lastPublishedAtByPoint.TryGetValue(sensorPointId, out var publishedAt)
                            ? publishedAt
                            : (DateTime?)null;
                        if (!SensorWorkflowHelper.PassDebounce(now, lastPublishedAt, _debounceWindowMs[sensorPointId])) {
                            continue;
                        }

                        var oldState = sensorInfo.State;
                        sensorInfo.State = newState;
                        // 步骤1补充：收集待更新条目，避免在 foreach 内修改字典引发异常。
                        pendingUpdates.Add((sensorPointId, sensorInfo, now));
                        changedEvents.Add(new SensorStateChangedEventArgs(
                            sensorInfo.Point,
                            _sensorNames[sensorPointId],
                            sensorInfo.Type,
                            oldState,
                            newState,
                            _triggerStates[sensorPointId],
                            occurredAtLocalMs));
                    }

                    // 步骤2：循环结束后统一写回状态，避免迭代器版本冲突。
                    foreach (var (pointId, newInfo, publishedAt) in pendingUpdates) {
                        _sensorInfos[pointId] = newInfo;
                        _lastPublishedAtByPoint[pointId] = publishedAt;
                    }
                }

                // 步骤3：在锁外发布变化事件，避免阻塞监控采样。
                foreach (var changedEvent in changedEvents) {
                    _ = _executor.Execute(
                        () => SensorStateChanged?.Invoke(this, changedEvent),
                        "LeadshaineSensorManager.SensorStateChanged");
                }

                // 步骤4：按 EMC 轮询间隔等待下一轮采样。
                await Task.Delay(nextDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 处理 EMC 状态变化事件。
        /// </summary>
        /// <param name="sender">事件发送方。</param>
        /// <param name="args">状态变化参数。</param>
        private void HandleEmcStatusChanged(object? sender, Core.Events.Emc.EmcStatusChangedEventArgs args) {
            if (args.NewStatus is not (Core.Enums.Emc.EmcControllerStatus.Disconnected or Core.Enums.Emc.EmcControllerStatus.Faulted)) {
                return;
            }

            PublishFault($"EMC 状态异常：{args.NewStatus}。", null);
        }

        /// <summary>
        /// 发布传感器故障事件并记录日志。
        /// </summary>
        /// <param name="message">故障消息。</param>
        /// <param name="exception">异常对象。</param>
        private void PublishFault(string message, Exception? exception) {
            SetStatus(SensorMonitoringStatus.Faulted);
            _logger.LogError(exception, "Leadshaine 传感器管理器异常：{Message}", message);
            _ = _executor.Execute(
                () => Faulted?.Invoke(this, new SensorFaultedEventArgs(message, exception, DateTime.Now)),
                "LeadshaineSensorManager.Faulted");
        }

        /// <summary>
        /// 切换传感器监控状态并发布状态事件。
        /// </summary>
        /// <param name="newStatus">新状态。</param>
        private void SetStatus(SensorMonitoringStatus newStatus) {
            var oldStatus = Status;
            if (oldStatus == newStatus) {
                return;
            }

            Status = newStatus;
            _ = _executor.Execute(
                () => MonitoringStatusChanged?.Invoke(
                    this,
                    new SensorMonitoringStatusChangedEventArgs(oldStatus, newStatus, DateTime.Now)),
                "LeadshaineSensorManager.MonitoringStatusChanged");
        }

        /// <summary>
        /// 解析触发电平字符串。
        /// </summary>
        /// <param name="triggerState">触发电平配置。</param>
        /// <returns>触发电平。</returns>
        private static IoState ParseTriggerState(string triggerState) {
            return string.Equals(triggerState, "Low", StringComparison.OrdinalIgnoreCase)
                ? IoState.Low
                : IoState.High;
        }

        /// <summary>
        /// 构建传感器点位编号。
        /// </summary>
        /// <param name="cardNo">板卡号。</param>
        /// <param name="portNo">端口号。</param>
        /// <param name="bitIndex">位索引。</param>
        /// <returns>点位编号。</returns>
        private static int BuildSensorPointNumber(ushort cardNo, ushort portNo, int bitIndex) {
            return cardNo * CardPointFactor + portNo * PortPointFactor + bitIndex;
        }

        /// <summary>
        /// 在对象释放后抛出异常。
        /// </summary>
        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(LeadshaineSensorManager));
            }
        }
    }
}
