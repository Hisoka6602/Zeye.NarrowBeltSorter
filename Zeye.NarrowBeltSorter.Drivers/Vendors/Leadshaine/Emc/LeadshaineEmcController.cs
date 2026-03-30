using Polly;
using Zeye.NarrowBeltSorter.Core.Enums.Emc;
using Zeye.NarrowBeltSorter.Core.Events.Emc;
using Zeye.NarrowBeltSorter.Core.Manager.Emc;
using Zeye.NarrowBeltSorter.Core.Models.Emc;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Utilities;
using DriverBindingOptions = Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc.Options.LeadshainePointBindingCollectionOptions;
using DriverPointBindingOptions = Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc.Options.LeadshainePointBindingOptions;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc {
    /// <summary>
    /// Leadshaine EMC 控制器实现。
    /// </summary>
    public sealed class LeadshaineEmcController : IEmcController {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetLogger(nameof(LeadshaineEmcController));
        private const int BitsPerPort = 32;
        /// <summary>
        /// LTDMC 文档中 dmc_read_inport 断链返回码。
        /// </summary>
        private const uint DisconnectedReadCode = 9;
        private const double ReconnectBackoffMultiplier = 1.6;
        private readonly object _stateLock = new();
        private readonly SafeExecutor _safeExecutor;
        private readonly IEmcHardwareAdapter _hardwareAdapter;
        private readonly LeadshaineEmcConnectionOptions _connectionOptions;
        private readonly Dictionary<string, DriverPointBindingOptions> _pointMap;
        private readonly Dictionary<(ushort CardNo, ushort PortNo), List<DriverPointBindingOptions>> _inputGroups = [];
        private readonly Dictionary<string, IoPointInfo> _latestPoints = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _registeredPointIds = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// 输入分组快照（volatile 保证可见性），仅在 SetMonitoredIoPointsAsync 时替换引用，监控循环无锁读取。
        /// </summary>
        private volatile (ushort CardNo, ushort PortNo, DriverPointBindingOptions[] Points)[]? _monitorGroupsSnapshot;
        private CancellationTokenSource? _monitoringCts;
        private Task? _monitoringTask;
        private int _isReconnectRunning;
        private bool _disposed;

        /// <summary>
        /// 初始化 Leadshaine EMC 控制器。
        /// </summary>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <param name="connectionOptions">连接配置。</param>
        /// <param name="pointBindings">点位绑定配置。</param>
        /// <param name="hardwareAdapter">硬件访问适配器。</param>
        public LeadshaineEmcController(
            SafeExecutor safeExecutor,
            LeadshaineEmcConnectionOptions connectionOptions,
            DriverBindingOptions pointBindings,
            IEmcHardwareAdapter? hardwareAdapter = null) {
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _connectionOptions = connectionOptions ?? throw new ArgumentNullException(nameof(connectionOptions));
            _hardwareAdapter = hardwareAdapter ?? new LeadshaineEmcHardwareAdapter();
            _pointMap = pointBindings?.Points?
                .Where(x => !string.IsNullOrWhiteSpace(x.PointId))
                .ToDictionary(x => x.PointId, x => x, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, DriverPointBindingOptions>(StringComparer.OrdinalIgnoreCase);
            Status = EmcControllerStatus.Uninitialized;
        }

        /// <inheritdoc />
        public EmcControllerStatus Status { get; private set; }

        /// <inheritdoc />
        public int FaultCode { get; private set; }

        /// <inheritdoc />
        public IReadOnlyCollection<IoPointInfo> MonitoredIoPoints {
            get {
                lock (_stateLock) {
                    return _latestPoints.Values.ToArray();
                }
            }
        }

        /// <inheritdoc />
        public event EventHandler<EmcStatusChangedEventArgs>? StatusChanged;

        /// <inheritdoc />
        public event EventHandler<EmcFaultedEventArgs>? Faulted;

        /// <inheritdoc />
        public event EventHandler<EmcInitializedEventArgs>? Initialized;

        /// <inheritdoc />
        public async ValueTask<bool> InitializeAsync(CancellationToken cancellationToken = default) {
            ThrowIfDisposed();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_connectionOptions.ConnectionTimeoutMs);

            // 步骤1：切换初始化状态并执行带重试的建连流程。
            SetStatus(EmcControllerStatus.Initializing, "开始初始化。");
            SetStatus(EmcControllerStatus.Connecting, "开始建立控制卡连接。");

            var delays = BuildInitializeRetryDelays();
            var retryPolicy = Policy
                .Handle<InvalidOperationException>()
                .WaitAndRetryAsync(delays, (_, span, retryAttempt, _) => {
                    Logger.Warn("Leadshaine EMC 初始化重试 attempt={0} delayMs={1}", retryAttempt, (int)span.TotalMilliseconds);
                });

            var initialized = await _safeExecutor.ExecuteAsync(
                async token => {
                    await retryPolicy.ExecuteAsync(async ct => {
                        var normalizedControllerIp = string.IsNullOrWhiteSpace(_connectionOptions.ControllerIp)
                            ? null
                            : _connectionOptions.ControllerIp;
                        var initCode = _hardwareAdapter.InitializeBoard(_connectionOptions.CardNo, normalizedControllerIp);
                        if (initCode != 0) {
                            throw new InvalidOperationException($"dmc_board_init/dmc_board_init_eth 返回码异常：{initCode}。");
                        }

                        var errorCode = (ushort)0;
                        var errorResult = _hardwareAdapter.GetErrorCode(_connectionOptions.CardNo, _connectionOptions.Channel, ref errorCode);
                        if (errorResult != 0 || errorCode != 0) {
                            _ = _hardwareAdapter.SoftReset(_connectionOptions.CardNo);
                            throw new InvalidOperationException($"nmc_get_errcode 异常：result={errorResult}, errorCode={errorCode}。");
                        }

                        await Task.CompletedTask.ConfigureAwait(false);
                    }, token).ConfigureAwait(false);
                },
                "LeadshaineEmcController.InitializeAsync",
                timeoutCts.Token,
                ex => PublishFault("初始化失败。", ex, -1)).ConfigureAwait(false);

            if (!initialized) {
                SetStatus(EmcControllerStatus.Faulted, "初始化失败。");
                return false;
            }

            // 步骤2：启动监控循环并发布初始化完成事件。
            StartMonitoringLoop();
            SetStatus(EmcControllerStatus.Connected, "初始化成功。");
            Initialized?.Invoke(this, new EmcInitializedEventArgs { InitializedAt = DateTime.Now });
            return true;
        }

        /// <inheritdoc />
        public async ValueTask<bool> ReconnectAsync(CancellationToken cancellationToken = default) {
            ThrowIfDisposed();
            SetStatus(EmcControllerStatus.Disconnected, "开始重连。");

            // 步骤1：先停止旧监控循环，避免并发轮询冲突。
            await StopMonitoringLoopAsync().ConfigureAwait(false);

            // 步骤2：执行指数退避重连。
            var delay = _connectionOptions.ReconnectBaseDelayMs;
            var maxDelay = _connectionOptions.ReconnectMaxDelayMs;
            var maxAttempts = GetMaxReconnectAttempts();
            for (var attempt = 1; attempt <= maxAttempts; attempt++) {
                cancellationToken.ThrowIfCancellationRequested();
                var ok = await InitializeAsync(cancellationToken).ConfigureAwait(false);
                if (ok) {
                    return true;
                }

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = Math.Min((int)Math.Ceiling(delay * ReconnectBackoffMultiplier), maxDelay);
            }

            SetStatus(EmcControllerStatus.Faulted, "重连失败。");
            return false;
        }

        /// <inheritdoc />
        public ValueTask<bool> SetMonitoredIoPointsAsync(
            IReadOnlyCollection<string> pointIds,
            CancellationToken cancellationToken = default) {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            // 步骤1：增量注册点位，并按输入点构建分组读取映射。
            lock (_stateLock) {
                foreach (var pointId in pointIds) {
                    if (string.IsNullOrWhiteSpace(pointId)) {
                        continue;
                    }

                    if (!_pointMap.TryGetValue(pointId, out var binding)) {
                        continue;
                    }

                    if (!_registeredPointIds.Add(pointId)) {
                        continue;
                    }

                    var area = binding.Binding.Area.Trim();
                    if (!string.Equals(area, "Input", StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    var key = (binding.Binding.CardNo, binding.Binding.PortNo);
                    if (!_inputGroups.TryGetValue(key, out var list)) {
                        list = [];
                        _inputGroups[key] = list;
                    }

                    list.Add(binding);
                }

                // 步骤2：重建监控分组快照（volatile 写，监控循环无需加锁即可读取）。
                _monitorGroupsSnapshot = BuildMonitorGroupsSnapshot();
            }

            return ValueTask.FromResult(true);
        }

        /// <inheritdoc />
        public async ValueTask<bool> WriteIoAsync(
            string pointId,
            bool value,
            CancellationToken cancellationToken = default) {
            ThrowIfDisposed();

            // 步骤1：校验点位存在且必须是输出区点位。
            if (!_pointMap.TryGetValue(pointId, out var binding)) {
                PublishFault($"写入失败：PointId={pointId} 未定义。", null, -2);
                return false;
            }

            if (!string.Equals(binding.Binding.Area, "Output", StringComparison.OrdinalIgnoreCase)) {
                PublishFault($"写入失败：PointId={pointId} 非输出区点位。", null, -3);
                return false;
            }

            // 步骤2：通过 SafeExecutor 执行底层写入。
            var writeOk = await _safeExecutor.ExecuteAsync(
                _ => {
                    var bitNo = checked((ushort)(binding.Binding.PortNo * BitsPerPort + binding.Binding.BitIndex));
                    var result = _hardwareAdapter.WriteOutBit(
                        binding.Binding.CardNo,
                        bitNo,
                        value ? (ushort)1 : (ushort)0);
                    if (result != 0) {
                        throw new InvalidOperationException($"dmc_write_outbit 返回码异常：{result}。");
                    }

                    return ValueTask.CompletedTask;
                },
                "LeadshaineEmcController.WriteIoAsync",
                cancellationToken,
                ex => PublishFault($"写入失败：PointId={pointId}。", ex, -4)).ConfigureAwait(false);

            return writeOk;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            await StopMonitoringLoopAsync().ConfigureAwait(false);
            _ = _hardwareAdapter.CloseBoard();
        }

        /// <summary>
        /// 启动后台监控循环。
        /// </summary>
        private void StartMonitoringLoop() {
            _monitoringCts?.Cancel();
            _monitoringCts?.Dispose();
            _monitoringCts = new CancellationTokenSource();
            _monitoringTask = Task.Run(() => MonitoringLoopAsync(_monitoringCts.Token), _monitoringCts.Token);
        }

        /// <summary>
        /// 停止后台监控循环。
        /// </summary>
        /// <returns>停止任务。</returns>
        private async ValueTask StopMonitoringLoopAsync() {
            if (_monitoringCts is null) {
                return;
            }

            _monitoringCts.Cancel();
            if (_monitoringTask is not null) {
                try {
                    await _monitoringTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    // 监控循环由取消令牌正常中断，不做额外处理。
                }
            }

            _monitoringCts.Dispose();
            _monitoringCts = null;
            _monitoringTask = null;
        }

        /// <summary>
        /// 监控循环：读取输入端口并更新快照。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>循环任务。</returns>
        private async Task MonitoringLoopAsync(CancellationToken cancellationToken) {
            while (!cancellationToken.IsCancellationRequested) {
                // 步骤1：读取 volatile 快照引用，无需加锁。
                var snapshot = _monitorGroupsSnapshot;
                if (snapshot is null || snapshot.Length == 0) {
                    await Task.Delay(_connectionOptions.PollingIntervalMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // 步骤2：按分组读取端口位图，遇到断链码时退出循环并异步触发重连。
                var snapshotTime = DateTime.Now;
                var disconnected = false;
                foreach (var (cardNo, portNo, points) in snapshot) {
                    cancellationToken.ThrowIfCancellationRequested();
                    var portValue = _hardwareAdapter.ReadInPort(cardNo, portNo);
                    if (portValue == DisconnectedReadCode) {
                        SetStatus(EmcControllerStatus.Disconnected, "检测到断链返回码。");
                        lock (_stateLock) {
                            _latestPoints.Clear();
                        }
                        TryStartReconnect();
                        disconnected = true;
                        break;
                    }

                    // 步骤3：将端口位图展开为点位快照，并更新共享只读视图。
                    lock (_stateLock) {
                        foreach (var binding in points) {
                            var bitSet = (portValue & (1u << binding.Binding.BitIndex)) != 0;
                            _latestPoints[binding.PointId] = new IoPointInfo {
                                PointId = binding.PointId,
                                Area = binding.Binding.Area,
                                CardNo = binding.Binding.CardNo,
                                PortNo = binding.Binding.PortNo,
                                BitIndex = binding.Binding.BitIndex,
                                Value = bitSet,
                                CapturedAt = snapshotTime
                            };
                        }
                    }
                }

                // 步骤4：断链时立即退出，由重连任务负责重启监控循环，避免持续无效轮询。
                if (disconnected) {
                    return;
                }

                await Task.Delay(_connectionOptions.PollingIntervalMs, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 切换控制器状态并发布事件。
        /// </summary>
        /// <param name="status">新状态。</param>
        /// <param name="reason">状态变化原因。</param>
        private void SetStatus(EmcControllerStatus status, string? reason) {
            var oldStatus = Status;
            if (oldStatus == status) {
                return;
            }

            Status = status;
            StatusChanged?.Invoke(this, new EmcStatusChangedEventArgs {
                OldStatus = oldStatus,
                NewStatus = status,
                ChangedAt = DateTime.Now,
                Reason = reason
            });
        }

        /// <summary>
        /// 发布故障事件并记录日志。
        /// </summary>
        /// <param name="message">故障消息。</param>
        /// <param name="exception">异常对象。</param>
        /// <param name="faultCode">故障码。</param>
        private void PublishFault(string message, Exception? exception, int faultCode) {
            FaultCode = faultCode;
            if (exception is null) {
                Logger.Error("Leadshaine EMC 故障 code={0} message={1}", faultCode, message);
            }
            else {
                Logger.Error(exception, "Leadshaine EMC 故障 code={0} message={1}", faultCode, message);
            }

            Faulted?.Invoke(this, new EmcFaultedEventArgs {
                FaultCode = faultCode,
                Message = message,
                FaultedAt = DateTime.Now,
                Exception = exception
            });
        }

        /// <summary>
        /// 构建初始化重试间隔序列。
        /// </summary>
        /// <returns>重试间隔数组。</returns>
        private TimeSpan[] BuildInitializeRetryDelays() {
            var retryCount = Math.Max(_connectionOptions.InitializeRetryCount, 0);
            if (retryCount == 0) {
                return [];
            }

            var delays = new TimeSpan[retryCount];
            var current = _connectionOptions.InitializeRetryDelayMs;
            for (var i = 0; i < retryCount; i++) {
                delays[i] = TimeSpan.FromMilliseconds(current);
                current = Math.Min(current * 2, _connectionOptions.ReconnectMaxDelayMs);
            }

            return delays;
        }

        /// <summary>
        /// 获取重连最大尝试次数（包含首次尝试）。
        /// </summary>
        /// <returns>最大尝试次数。</returns>
        private int GetMaxReconnectAttempts() {
            return Math.Max(_connectionOptions.InitializeRetryCount + 1, 1);
        }

        /// <summary>
        /// 触发重连任务（同一时刻仅允许一个重连任务执行）。
        /// </summary>
        private void TryStartReconnect() {
            if (Interlocked.CompareExchange(ref _isReconnectRunning, 1, 0) != 0) {
                return;
            }

            _ = Task.Run(async () => {
                try {
                    _ = await ReconnectAsync().ConfigureAwait(false);
                }
                catch (Exception ex) {
                    PublishFault("断链重连任务执行失败。", ex, -5);
                }
                finally {
                    Interlocked.Exchange(ref _isReconnectRunning, 0);
                }
            });
        }

        /// <inheritdoc />
        public bool TryGetMonitoredPoint(string pointId, out IoPointInfo info) {
            lock (_stateLock) {
                return _latestPoints.TryGetValue(pointId, out info);
            }
        }

        /// <summary>
        /// 构建监控分组快照数组（必须在 _stateLock 内调用）。
        /// </summary>
        /// <returns>监控分组快照。</returns>
        private (ushort CardNo, ushort PortNo, DriverPointBindingOptions[] Points)[] BuildMonitorGroupsSnapshot() {
            var result = new (ushort CardNo, ushort PortNo, DriverPointBindingOptions[] Points)[_inputGroups.Count];
            var i = 0;
            foreach (var kv in _inputGroups) {
                result[i++] = (kv.Key.CardNo, kv.Key.PortNo, kv.Value.ToArray());
            }

            return result;
        }

        /// <summary>
        /// 在释放后抛出对象已释放异常。
        /// </summary>
        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(LeadshaineEmcController));
            }
        }
    }

}
