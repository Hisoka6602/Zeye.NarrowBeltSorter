using Polly;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.Track;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Utilities.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;
using LoopTrackManagerHostedServiceLogger = Microsoft.Extensions.Logging.ILogger<Zeye.NarrowBeltSorter.Execution.Services.LoopTrackManagerHostedService>;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 环形轨道管理后台服务。
    /// </summary>
    public class LoopTrackManagerHostedService : BackgroundService {

        /// <summary>
        /// 状态分类日志事件编号（41xx 段用于 LoopTrack 分类日志）。
        /// </summary>
        private static readonly EventId LoopTrackStatusEventId = new(4101, "looptrack-status");

        /// <summary>
        /// 故障分类日志事件编号（41xx 段用于 LoopTrack 分类日志）。
        /// </summary>
        private static readonly EventId LoopTrackFaultEventId = new(4103, "looptrack-fault");

        /// <summary>
        /// 速度监测日志事件编号（实时速度与调速统一落盘）。
        /// </summary>
        private static readonly EventId LoopTrackSpeedEventId = new(4104, "looptrack-speed");

        private readonly ILogger<LoopTrackManagerHostedService> _logger;
        private readonly SafeExecutor _safeExecutor;
        private readonly IOptionsMonitor<LoopTrackServiceOptions> _optionsMonitor;
        private readonly IDisposable _optionsChangedRegistration;
        private readonly ISystemStateManager _systemStateManager;
        private readonly ILoopTrackManagerAccessor _loopTrackAccessor;
        private LoopTrackServiceOptions _optionsSnapshot;
        private LoopTrackServiceOptions _options => Volatile.Read(ref _optionsSnapshot);

        /// <summary>
        /// 当前服务持有的环轨管理器实例；受保护可供派生类访问，生命周期释放与置空由服务停止流程统一控制，禁止跨线程替换。
        /// 请勿直接赋值此字段，统一使用 <see cref="SetManager"/> 方法以同步更新访问器。
        /// </summary>
        protected ILoopTrackManager? _manager;

        private int _stopRequestedFlag;

        /// <summary>
        /// 环轨启停控制互斥量：防止轮询循环与 StateChanged 事件驱动并发执行启停控制逻辑。
        /// </summary>
        private readonly SemaphoreSlim _runControlSemaphore = new(1, 1);

        /// <summary>
        /// 待处理的立即停机标记：StateChanged 触发停机请求时，若轮询循环持有锁，
        /// 设置此标记使轮询释放锁后立即再执行一次停机控制，确保停机请求不丢失。
        /// 使用 int 配合 Interlocked 确保跨线程原子操作（1=待处理，0=无）。
        /// </summary>
        private int _pendingImmediateStop;

        /// <summary>
        /// 主服务日志组件。
        /// </summary>
        protected ILogger<LoopTrackManagerHostedService> Logger => _logger;

        /// <summary>
        /// 全局安全执行器。
        /// </summary>
        protected SafeExecutor SafeExecutor => _safeExecutor;

        /// <summary>
        /// 主服务配置。
        /// </summary>
        protected LoopTrackServiceOptions Options => _options;

        /// <summary>
        /// 初始化环形轨道管理后台服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <param name="optionsMonitor">服务配置。</param>
        /// <param name="systemStateManager">系统状态管理器。</param>
        /// <param name="loopTrackAccessor">环轨管理器访问器，用于向其他服务暴露当前管理器实例。</param>
        public LoopTrackManagerHostedService(
            LoopTrackManagerHostedServiceLogger logger,
            SafeExecutor safeExecutor,
            IOptionsMonitor<LoopTrackServiceOptions> optionsMonitor,
            ISystemStateManager systemStateManager,
            ILoopTrackManagerAccessor loopTrackAccessor) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _optionsSnapshot = _optionsMonitor.CurrentValue ?? throw new InvalidOperationException("LoopTrackServiceOptions 不能为空。");
            _optionsChangedRegistration = _optionsMonitor.OnChange(RefreshOptionsSnapshot) ?? throw new InvalidOperationException("LoopTrackServiceOptions.OnChange 订阅失败。");
            _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));
            _loopTrackAccessor = loopTrackAccessor ?? throw new ArgumentNullException(nameof(loopTrackAccessor));
        }

        /// <summary>
        /// 刷新环轨服务配置快照。
        /// </summary>
        /// <param name="options">最新环轨服务配置。</param>
        private void RefreshOptionsSnapshot(LoopTrackServiceOptions options) {
            Volatile.Write(ref _optionsSnapshot, options);
        }

        /// <summary>
        /// 设置当前管理器实例并同步更新访问器，确保其他服务可感知实例变化。
        /// 所有对 <see cref="_manager"/> 字段的赋值均须通过此方法进行。
        /// </summary>
        /// <param name="manager">新管理器实例；传 null 表示清空。</param>
        protected void SetManager(ILoopTrackManager? manager) {
            _manager = manager;
            _loopTrackAccessor.SetManager(manager);
        }

        /// <summary>
        /// 执行后台服务主循环。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            if (!_options.Enabled) {
                _logger.LogInformation("LoopTrack 后台服务已禁用。");
                return;
            }

            if (!TryValidateOptions(_options, out var validationMessage)) {
                _logger.LogError("LoopTrack 配置无效，后台服务退出。原因：{ValidationMessage}", validationMessage);
                return;
            }

            // 步骤1：构造管理器并绑定事件，确保连接、运行、稳速、速度与故障事件全量接入。
            var pollingInterval = TimeSpan.FromMilliseconds(_options.PollingIntervalMs);
            var infoStatusInterval = TimeSpan.FromMilliseconds(_options.Logging.InfoStatusIntervalMs);
            var debugStatusInterval = TimeSpan.FromMilliseconds(_options.Logging.DebugStatusIntervalMs);
            var manager = CreateManager(pollingInterval);
            SetManager(manager);
            BindEvents(manager);

            try {
                _logger.LogInformation(
                    "LoopTrack 启动配置快照 OperationId={OperationId} Track={TrackName} Transport={Transport} Host={RemoteHost} SerialPort={SerialPort} Slaves={SlaveAddresses} SpeedAggregateStrategy={SpeedAggregateStrategy} TimeoutMs={TimeoutMs} RetryCount={RetryCount} PollingIntervalMs={PollingIntervalMs} PidKp={PidKp} PidKi={PidKi} PidKd={PidKd}",
                    CreateOperationId(),
                    _options.TrackName,
                    _options.LeiMaConnection.Transport,
                    _options.LeiMaConnection.RemoteHost,
                    _options.LeiMaConnection.SerialRtu.PortName,
                    string.Join(",", _options.LeiMaConnection.SlaveAddresses),
                    _options.LeiMaConnection.SpeedAggregateStrategy,
                    _options.LeiMaConnection.TimeoutMs,
                    _options.LeiMaConnection.RetryCount,
                    _options.PollingIntervalMs,
                    _options.Pid.Kp,
                    _options.Pid.Ki,
                    _options.Pid.Kd);

                // 步骤2：执行配置化连接重试，连接失败时退出并释放资源。
                var connected = await ConnectWithRetryAsync(manager, stoppingToken);
                if (!connected) {
                    return;
                }

                // 步骤3：AutoStart 流程执行 Start -> SetTargetSpeed，失败时执行补偿停机与断连。
                if (_options.AutoStart && _systemStateManager.CurrentState == SystemState.Running) {
                    var autoStartSuccess = await TryAutoStartAsync(manager, stoppingToken);
                    if (!autoStartSuccess) {
                        return;
                    }
                }
                else if (_options.AutoStart) {
                    _logger.LogInformation(
                        "LoopTrack 跳过自动启动：当前系统状态为 {CurrentState}，仅在 Running 状态启动。",
                        _systemStateManager.CurrentState);
                }

                // 步骤4：订阅系统状态变更事件，在停止/急停状态切换时立即触发停机控制，
                // 不等待下一个轮询 tick，减少因轮询延迟导致的停机响应延迟。
                // 设计说明：
                // - _pendingImmediateStop 使用 Interlocked 保证原子读写，防止标记丢失。
                // - WaitAsync(0) 非阻塞尝试获取信号量；若轮询循环正在持锁，则 pending 标记由
                //   轮询释放锁后的 Interlocked.Exchange 原子检查并消费，确保停机请求不丢失。
                // - lastImmediateStopTask 追踪最近一次 fire-and-forget 任务，使用 Interlocked
                //   保证引用的原子更新；finally 中等待其完成后再执行 SafeStopAndDisconnectAsync，
                //   避免与 manager 并发竞争。
                Task lastImmediateStopTask = Task.CompletedTask;
                EventHandler<Core.Events.System.StateChangeEventArgs> stateChangedHandler =
                    (_, args) => {
                        if (args.NewState != SystemState.Running && !stoppingToken.IsCancellationRequested) {
                            _logger.LogInformation(
                                "LoopTrack 检测到系统状态切换为非运行态，立即触发停机控制 OldState={OldState} NewState={NewState}。",
                                args.OldState,
                                args.NewState);
                            // 原子设置 pending 标记，确保即使无法立即执行也不丢失停机请求。
                            Interlocked.Exchange(ref _pendingImmediateStop, 1);
                            var immediateTask = _safeExecutor.ExecuteAsync(
                                () => TryImmediateStopControlAsync(manager, stoppingToken),
                                "LoopTrackManagerHostedService.StateChanged.ImmediateStop");
                            // 使用 Interlocked.Exchange 原子更新最后一次立即停机任务引用。
                            Interlocked.Exchange(ref lastImmediateStopTask, immediateTask);
                        }
                    };
                _systemStateManager.StateChanged += stateChangedHandler;
                try {
                    // 步骤5：持续状态监测与结构化日志输出，Info 常态输出，Debug 受配置频率控制。
                    await MonitorStatusLoopAsync(manager, pollingInterval, infoStatusInterval, debugStatusInterval,
                        stoppingToken);
                }
                finally {
                    _systemStateManager.StateChanged -= stateChangedHandler;
                    // 等待最后一次立即停机任务完成，确保与后续 SafeStopAndDisconnectAsync 无并发竞争。
                    // Interlocked.Exchange 写入已保证可见性，直接 await 本地引用即可。
                    await lastImmediateStopTask.ConfigureAwait(false);
                }
            }
            finally {
                var currentManager = _manager;
                if (currentManager is not null) {
                    await SafeStopAndDisconnectAsync(currentManager, "LoopTrackManagerHostedService.ExecuteAsync.Finally",
                        CancellationToken.None);
                    await ReleaseManagerSafelyAsync(currentManager);
                    SetManager(null);
                }
            }
        }

        /// <summary>
        /// 停止后台服务并释放资源。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        public override async Task StopAsync(CancellationToken cancellationToken) {
            if (Interlocked.Exchange(ref _stopRequestedFlag, 1) == 1) {
                _logger.LogInformation("LoopTrack 停止流程已执行，跳过重复停止。");
                return;
            }

            // 步骤1：先触发并等待 ExecuteAsync 停止，避免监测循环与释放并发。
            await base.StopAsync(cancellationToken);

            var manager = _manager;
            if (manager is not null) {
                // 步骤2：优先停机并断连，失败不中断后续释放。
                await SafeStopAndDisconnectAsync(manager, "LoopTrackManagerHostedService.Stop", cancellationToken);

                // 步骤3：释放资源，保证后台任务结束。
                await ReleaseManagerSafelyAsync(manager);
                SetManager(null);
            }
        }

        /// <summary>
        /// 构造雷码环轨管理器实例。
        /// </summary>
        /// <param name="pollingInterval">轮询周期。</param>
        /// <returns>管理器实例。</returns>
        protected virtual ILoopTrackManager CreateManager(TimeSpan pollingInterval) {
            var connection = _options.LeiMaConnection;
            var connectionOptions = new LoopTrackConnectionOptions {
                SlaveAddress = connection.SlaveAddresses[0],
                TimeoutMilliseconds = connection.TimeoutMs,
                RetryCount = connection.RetryCount
            };
            var adapters = connection.SlaveAddresses
                .Select(slaveAddress => (SlaveAddress: slaveAddress, Adapter: CreateAdapter(connection, slaveAddress)))
                .ToList();

            return new LeiMaLoopTrackManager(
                trackName: _options.TrackName,
                modbusClient: adapters[0].Adapter,
                safeExecutor: _safeExecutor,
                connectionOptions: connectionOptions,
                pidOptions: _options.Pid,
                maxOutputHz: connection.MaxOutputHz,
                maxTorqueRawUnit: connection.MaxTorqueRawUnit,
                pollingInterval: pollingInterval,
                torqueSetpointWriteInterval: TimeSpan.FromMilliseconds(connection.TorqueSetpointWriteIntervalMs),
                stabilizedToleranceMmps: _options.StabilizedToleranceMmps,
                stabilizationWindow: TimeSpan.FromMilliseconds(_options.StabilizedWindowMs),
                slaveClients: adapters,
                speedAggregateStrategy: connection.SpeedAggregateStrategy);
        }

        /// <summary>
        /// 按传输模式创建 Modbus 适配器。
        /// </summary>
        /// <param name="connection">连接配置。</param>
        /// <returns>Modbus 适配器。</returns>
        protected virtual ILeiMaModbusClientAdapter CreateAdapter(LoopTrackLeiMaConnectionOptions connection) {
            return CreateAdapter(connection, connection.SlaveAddresses[0]);
        }

        /// <summary>
        /// 按传输模式与指定从站地址创建 Modbus 适配器。
        /// </summary>
        /// <param name="connection">连接配置。</param>
        /// <param name="slaveAddress">从站地址。</param>
        /// <returns>Modbus 适配器。</returns>
        protected virtual ILeiMaModbusClientAdapter CreateAdapter(LoopTrackLeiMaConnectionOptions connection, byte slaveAddress) {
            if (string.Equals(connection.Transport, LoopTrackLeiMaTransportModes.SerialRtu, StringComparison.OrdinalIgnoreCase)) {
                var serial = connection.SerialRtu;
                return CreateSerialRtuAdapter(serial, connection, slaveAddress);
            }

            return CreateTcpGatewayAdapter(connection, slaveAddress);
        }

        /// <summary>
        /// 创建 TCP 网关适配器。
        /// </summary>
        /// <param name="connection">连接配置。</param>
        /// <returns>适配器实例。</returns>
        protected virtual ILeiMaModbusClientAdapter CreateTcpGatewayAdapter(LoopTrackLeiMaConnectionOptions connection, byte slaveAddress) {
            return new LeiMaModbusClientAdapter(
                connection.RemoteHost,
                slaveAddress,
                connection.TimeoutMs,
                connection.RetryCount);
        }

        /// <summary>
        /// 创建串口 RTU 适配器。
        /// </summary>
        /// <param name="serial">串口参数。</param>
        /// <param name="connection">连接配置。</param>
        /// <returns>适配器实例。</returns>
        protected virtual ILeiMaModbusClientAdapter CreateSerialRtuAdapter(
            LoopTrackLeiMaSerialRtuOptions serial,
            LoopTrackLeiMaConnectionOptions connection,
            byte slaveAddress) {
            return new LeiMaModbusClientAdapter(
                serial.PortName,
                serial.BaudRate,
                serial.Parity,
                serial.DataBits,
                serial.StopBits,
                slaveAddress,
                connection.TimeoutMs,
                connection.RetryCount);
        }

        /// <summary>
        /// 执行自动启动流程。
        /// </summary>
        /// <param name="manager">环轨管理器。</param>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>自动启动是否成功。</returns>
        private async Task<bool> TryAutoStartAsync(ILoopTrackManager manager, CancellationToken stoppingToken) {
            var operationId = CreateOperationId();
            var stage = "LoopTrackManagerHostedService.AutoStart";
            var transport = _options.LeiMaConnection.Transport;
            var slaveAddresses = string.Join(",", _options.LeiMaConnection.SlaveAddresses);
            var started = await _safeExecutor.ExecuteAsync(
                token => manager.StartAsync(token),
                "LoopTrackManagerHostedService.StartAsync",
                false,
               stoppingToken);

            if (!started.Success || !started.Result) {
                _logger.LogWarning(
                    LoopTrackFaultEventId,
                    "LoopTrack 自动启动失败 OperationId={OperationId} Stage={Stage} Step={Step} Transport={Transport} SlaveAddresses={SlaveAddresses} TargetSpeedMmps={TargetSpeedMmps} RetryAttempt={RetryAttempt} ElapsedMs={ElapsedMs} ExceptionType={ExceptionType} ExceptionMessage={ExceptionMessage}，已触发补偿链路。",
                    operationId,
                    stage,
                    "StartAsync",
                    transport,
                    slaveAddresses,
                    _options.TargetSpeedMmps,
                    1,
                    0,
                    "SafeExecutorFailure",
                    "StartAsync  返回失败。");
                return false;
            }
            var setSpeedResult = await _safeExecutor.ExecuteAsync(
                token => manager.SetTargetSpeedAsync(_options.TargetSpeedMmps, token),
                "LoopTrackManagerHostedService.SetTargetSpeedAsync",
                false,
               stoppingToken);

            if (!setSpeedResult.Success || !setSpeedResult.Result) {
                _logger.LogWarning(
                    LoopTrackFaultEventId,
                    "LoopTrack 自动设速失败 OperationId={OperationId} Stage={Stage} Step={Step} Transport={Transport} SlaveAddresses={SlaveAddresses} RetryAttempt={RetryAttempt} ElapsedMs={ElapsedMs} ExceptionType={ExceptionType} ExceptionMessage={ExceptionMessage}，已触发补偿链路。",
                    operationId,
                    stage,
                    "SetTargetSpeedAsync",
                    transport,
                    slaveAddresses,

                    1,
                    0,
                    "SafeExecutorFailure",
                    "SetTargetSpeedAsync 返回失败。");
                return false;
            }

            _logger.LogInformation(
                LoopTrackStatusEventId,
                "LoopTrack 自动启动完成 OperationId={OperationId} Stage={Stage} Transport={Transport} SlaveAddresses={SlaveAddresses} TargetSpeedMmps={TargetSpeedMmps}。",
                operationId,
                stage,
                transport,
                slaveAddresses,
                _options.TargetSpeedMmps);
            return true;
        }

        /// <summary>
        /// 执行状态监测循环。
        /// </summary>
        /// <param name="manager">环轨管理器。</param>
        /// <param name="pollingInterval">轮询周期。</param>
        /// <param name="debugStatusInterval">调试日志输出间隔。</param>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task MonitorStatusLoopAsync(
            ILoopTrackManager manager,
            TimeSpan pollingInterval,
            TimeSpan infoStatusInterval,
            TimeSpan debugStatusInterval,
            CancellationToken stoppingToken) {
            using var timer = new PeriodicTimer(pollingInterval);
            var statusWatch = Stopwatch.StartNew();
            var transport = _options.LeiMaConnection.Transport;
            var slaveAddresses = string.Join(",", _options.LeiMaConnection.SlaveAddresses);
            var infoIntervalMs = (long)infoStatusInterval.TotalMilliseconds;
            var debugIntervalMs = (long)debugStatusInterval.TotalMilliseconds;
            var enableVerboseStatus = _options.Logging.EnableVerboseStatus;
            var enableRealtimeSpeedLog = _options.Logging.EnableRealtimeSpeedLog;
            var enablePidTuningLog = _options.Logging.EnablePidTuningLog;
            var instabilityThreshold = _options.Logging.UnstableDeviationThresholdMmps;
            var instabilityDurationMs = _options.Logging.UnstableDurationMs;
            var realtimeSpeedLogIntervalMs = (long)_options.Logging.RealtimeSpeedLogIntervalMs;
            var pidTuningLogIntervalMs = (long)_options.Logging.PidTuningLogIntervalMs;
            var nextInfoLogElapsedMs = infoIntervalMs;
            var nextDebugLogElapsedMs = debugIntervalMs;
            var nextRealtimeSpeedLogElapsedMs = realtimeSpeedLogIntervalMs;
            var nextPidTuningLogElapsedMs = pidTuningLogIntervalMs;
            var pollingIntervalMs = (long)pollingInterval.TotalMilliseconds;
            var unstableElapsedMs = 0L;
            var unstableLogged = false;
            var invalidPollingIntervalLogged = false;

            try {
                while (await timer.WaitForNextTickAsync(stoppingToken)) {
                    // 步骤0：加锁防止与 StateChanged 事件驱动的立即停机并发执行启停控制。
                    await ExecuteRunControlWithSemaphoreAsync(manager, stoppingToken).ConfigureAwait(false);
                    // 步骤0b：原子检查并消费待处理停机标记；若 StateChanged 在轮询持锁期间
                    // 设置了标记（未能立即执行），则此处补偿执行一次，确保停机请求不丢失。
                    // 使用 Interlocked.Exchange 保证检查与清除的原子性，消除竞态窗口。
                    if (Interlocked.Exchange(ref _pendingImmediateStop, 0) == 1) {
                        await ExecuteRunControlWithSemaphoreAsync(manager, stoppingToken).ConfigureAwait(false);
                    }
                    _safeExecutor.Execute(
                        () => {
                            // 步骤1：采集状态快照，供采样日志复用，避免重复属性读取。
                            var trackName = manager.TrackName;
                            var connectionStatus = manager.ConnectionStatus;
                            var runStatus = manager.RunStatus;
                            var stabilizationStatus = manager.StabilizationStatus;
                            var targetSpeedMmps = manager.TargetSpeedMmps;
                            var realTimeSpeedMmps = manager.RealTimeSpeedMmps;
                            var stabilizationElapsed = manager.StabilizationElapsed;
                            var speedDeviationMmps = targetSpeedMmps - realTimeSpeedMmps;
                            var deviationAbsMmps = Math.Abs(speedDeviationMmps);
                            var systemState = _systemStateManager.CurrentState;

                            if (deviationAbsMmps > instabilityThreshold) {
                                if (pollingIntervalMs > 0L) {
                                    unstableElapsedMs += pollingIntervalMs;
                                }
                                else if (!invalidPollingIntervalLogged) {
                                    _logger.LogWarning("LoopTrack 失稳计时未累加：PollingIntervalMs 非法，值={PollingIntervalMs}。", pollingIntervalMs);
                                    invalidPollingIntervalLogged = true;
                                }

                                if (!unstableLogged && unstableElapsedMs >= instabilityDurationMs) {
                                    _logger.LogWarning(
                                        LoopTrackFaultEventId,
                                        "LoopTrack 失稳告警 OperationId={OperationId} Stage={Stage} Transport={Transport} SlaveAddresses={SlaveAddresses} Name={TrackName} Target={TargetSpeedMmps}mm/s RealTime={RealTimeSpeedMmps}mm/s Deviation={SpeedDeviationMmps}mm/s Threshold={ThresholdMmps}mm/s DurationMs={DurationMs} 最近采样摘要={RecentSampleSummary} PID输出命令={PidCommandOutput}raw PID输出限幅={PidOutputClamped} 运行快照={RuntimeSnapshot}",
                                        CreateOperationId(),
                                        "LoopTrackManagerHostedService.MonitorStatusLoop.Unstable",
                                        transport,
                                        slaveAddresses,
                                        trackName,
                                        targetSpeedMmps,
                                        realTimeSpeedMmps,
                                        speedDeviationMmps,
                                        instabilityThreshold,
                                        unstableElapsedMs,
                                        $"Target={targetSpeedMmps:F2};Real={realTimeSpeedMmps:F2};Gap={speedDeviationMmps:F2};AbsGap={deviationAbsMmps:F2}",
                                        manager.PidLastCommandOutput,
                                        manager.PidLastOutputClamped,
                                        $"Conn={connectionStatus};Run={runStatus};Stabilization={stabilizationStatus};UpdatedAt={manager.PidLastUpdatedAt}");
                                    unstableLogged = true;
                                }
                            }
                            else {
                                unstableElapsedMs = 0L;
                                unstableLogged = false;
                            }

                            // 步骤2：按 Info 采样间隔输出常规状态日志，降低高频日志开销。
                            if (statusWatch.ElapsedMilliseconds >= nextInfoLogElapsedMs) {
                                _logger.LogInformation(
                                    LoopTrackStatusEventId,
                                    "LoopTrack状态 Stage={Stage} Transport={Transport} SlaveAddresses={SlaveAddresses} Name={TrackName} Conn={ConnectionStatus} Run={RunStatus} Stabilization={StabilizationStatus} StabilizationElapsed={StabilizationElapsed} Target={TargetSpeedMmps}mm/s RealTime={RealTimeSpeedMmps}mm/s Deviation={SpeedDeviationMmps}mm/s",
                                    "LoopTrackManagerHostedService.MonitorStatusLoop.Status",
                                    transport,
                                    slaveAddresses,
                                    trackName,
                                    connectionStatus,
                                    runStatus,
                                    stabilizationStatus,
                                    stabilizationElapsed,
                                    targetSpeedMmps,
                                    realTimeSpeedMmps,
                                    speedDeviationMmps);

                                nextInfoLogElapsedMs = statusWatch.ElapsedMilliseconds + infoIntervalMs;
                            }

                            // 步骤3：按 Debug 采样间隔输出详细状态日志。
                            if (enableVerboseStatus && statusWatch.ElapsedMilliseconds >= nextDebugLogElapsedMs) {
                                _logger.LogDebug(
                                    "LoopTrack调试状态 Name={TrackName} Conn={ConnectionStatus} Run={RunStatus} Stabilization={StabilizationStatus} StabilizationElapsed={StabilizationElapsed} Target={TargetSpeedMmps}mm/s RealTime={RealTimeSpeedMmps}mm/s Deviation={SpeedDeviationMmps}mm/s",
                                    trackName,
                                    connectionStatus,
                                    runStatus,
                                    stabilizationStatus,
                                    stabilizationElapsed,
                                    targetSpeedMmps,
                                    realTimeSpeedMmps,
                                    speedDeviationMmps);

                                nextDebugLogElapsedMs = statusWatch.ElapsedMilliseconds + debugIntervalMs;
                            }

                            // 步骤4：按配置频率输出实时速度日志。
                            if ((enableRealtimeSpeedLog &&
                                 statusWatch.ElapsedMilliseconds >= nextRealtimeSpeedLogElapsedMs) &&
                                ShouldWriteRealtimeSpeedLog(systemState, runStatus)) {
                                _logger.LogInformation(
                                    LoopTrackSpeedEventId,
                                    "LoopTrack实时速度日志 阶段={阶段} 传输模式={传输模式} 从站列表={从站列表} 轨道名称={轨道名称} 系统状态={系统状态} 目标速度={目标速度}mm/s 实时速度={实时速度}mm/s 速度偏差={速度偏差}mm/s 运行状态={运行状态} 稳速状态={稳速状态}",
                                    "LoopTrackManagerHostedService.MonitorStatusLoop.RealTime",
                                    transport,
                                    slaveAddresses,
                                    trackName,
                                    systemState,
                                    targetSpeedMmps,
                                    realTimeSpeedMmps,
                                    speedDeviationMmps,
                                    runStatus,
                                    stabilizationStatus);

                                nextRealtimeSpeedLogElapsedMs = statusWatch.ElapsedMilliseconds + realtimeSpeedLogIntervalMs;
                            }

                            // 步骤5：按配置频率输出 PID 调参日志。
                            if (enablePidTuningLog && statusWatch.ElapsedMilliseconds >= nextPidTuningLogElapsedMs && manager.PidLastUpdatedAt.HasValue) {
                                _logger.LogInformation(
                                    LoopTrackSpeedEventId,
                                    "LoopTrack调速日志 阶段={阶段} 传输模式={传输模式} 从站列表={从站列表} 轨道名称={轨道名称} 比例输出={比例输出}Hz 积分输出={积分输出}Hz 微分输出={微分输出}Hz 速度误差={速度误差}mm/s 命令输出={命令输出}raw 限幅前输出={限幅前输出}raw 是否限幅={是否限幅} 更新时间={更新时间}",
                                    "LoopTrackManagerHostedService.MonitorStatusLoop.Pid",
                                    transport,
                                    slaveAddresses,
                                    trackName,
                                    manager.PidLastProportionalHz,
                                    manager.PidLastIntegralHz,
                                    manager.PidLastDerivativeHz,
                                    manager.PidLastErrorMmps,
                                    manager.PidLastCommandOutput,
                                    manager.PidLastUnclampedOutput,
                                    manager.PidLastOutputClamped,
                                    manager.PidLastUpdatedAt);

                                nextPidTuningLogElapsedMs = statusWatch.ElapsedMilliseconds + pidTuningLogIntervalMs;
                            }
                        },
                        "LoopTrackManagerHostedService.MonitorStatusLoop");
                }
            }
            catch (OperationCanceledException) {
                _logger.LogInformation("LoopTrack 后台服务收到停止信号。");
            }
        }

        /// <summary>
        /// 根据系统状态驱动环轨启停：Running 时以目标速度运行，Maintenance 时以检修速度运行，其他状态停止。
        /// </summary>
        /// <param name="manager">环轨管理器。</param>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        internal async Task ApplySystemStateRunControlAsync(ILoopTrackManager manager, CancellationToken stoppingToken) {
            // 步骤1：读取系统目标状态与设备当前状态，先进行最小必要的提前返回判断。
            var currentState = _systemStateManager.CurrentState;
            var shouldRun = currentState == SystemState.Running || currentState == SystemState.Maintenance;
            var isRunning = manager.RunStatus == LoopTrackRunStatus.Running;
            var isConnected = manager.ConnectionStatus == LoopTrackConnectionStatus.Connected;
            if (shouldRun) {
                if (isRunning && isConnected) {
                    return;
                }
            }
            else if (!isRunning && !isConnected) {
                return;
            }

            if (shouldRun) {
                // 步骤2：运行态/检修态下优先保证连接可用，再执行启动与目标速度下发。
                if (!isConnected) {
                    var connected = await ConnectWithRetryAsync(manager, stoppingToken);
                    if (!connected) {
                        _logger.LogWarning(
                            "LoopTrack 系统状态驱动连接失败：SystemState={SystemState} ConnectionStatus={ConnectionStatus}。",
                            currentState,
                            manager.ConnectionStatus);
                        return;
                    }
                }

                var startResult = await _safeExecutor.ExecuteAsync(
                    token => manager.StartAsync(token),
                    "LoopTrackManagerHostedService.SystemState.StartAsync",
                    false,
                    stoppingToken);
                if (!startResult.Success || !startResult.Result) {
                    _logger.LogWarning(
                        "LoopTrack 系统状态驱动启动失败：SystemState={SystemState} RunStatus={RunStatus}。",
                        currentState,
                        manager.RunStatus);
                    return;
                }

                // 步骤3：按状态选择目标速度：Maintenance 使用检修速度，Running 使用正常速度。
                var targetSpeed = currentState == SystemState.Maintenance
                    ? _options.MaintenanceSpeedMmps
                    : _options.TargetSpeedMmps;
                var setSpeedResult = await _safeExecutor.ExecuteAsync(
                    token => manager.SetTargetSpeedAsync(targetSpeed, token),
                    "LoopTrackManagerHostedService.SystemState.SetTargetSpeedAsync",
                    false,
                    stoppingToken);
                if (!setSpeedResult.Success || !setSpeedResult.Result) {
                    _logger.LogWarning(
                        "LoopTrack 系统状态驱动设速失败：SystemState={SystemState} TargetSpeedMmps={TargetSpeedMmps}。",
                        currentState,
                        targetSpeed);
                }

                return;
            }

            // 步骤3：非运行态须保证通讯可用后停机，再断开连接。
            // 安全关键：停机命令必须成功确认后才可断开连接；通讯断开时先重连再停机；
            // 若无法停机，不断开连接，保持 RunStatus=Running，确保下一轮询周期继续重试。
            if (!isConnected) {
                _logger.LogWarning(
                    LoopTrackFaultEventId,
                    "LoopTrack 停机前检测到通讯断开，尝试重连以下发停机命令 SystemState={SystemState}。",
                    currentState);
                var reconnected = await ConnectWithRetryAsync(manager, stoppingToken);
                if (!reconnected) {
                    _logger.LogCritical(
                        LoopTrackFaultEventId,
                        "LoopTrack 停机命令无法下发！通讯断开且重连失败，轨道可能仍在物理运行！SystemState={SystemState} RunStatus={RunStatus}。请立即检查通讯链路并手动停机！",
                        currentState,
                        manager.RunStatus);
                    // 不调用 DisconnectAsync，保持 RunStatus=Running，确保下一轮询周期继续重试停机。
                    return;
                }

                // 重连耗时较长，需重新读取系统状态；若状态已切回 Running 或 Maintenance，
                // 则放弃本次停机控制，由下一轮询周期按最新状态处理。
                var stateAfterReconnect = _systemStateManager.CurrentState;
                if (stateAfterReconnect == SystemState.Running || stateAfterReconnect == SystemState.Maintenance) {
                    _logger.LogInformation(
                        "LoopTrack 重连期间系统状态已切回 {SystemState}，放弃停机控制，由轮询循环按最新状态处理。",
                        stateAfterReconnect);
                    return;
                }
            }

            var zeroSpeedResult = await _safeExecutor.ExecuteAsync(
                token => manager.SetTargetSpeedAsync(0m, token),
                "LoopTrackManagerHostedService.SystemState.SetTargetSpeedZeroAsync",
                false,
                stoppingToken);
            if (!zeroSpeedResult.Success || !zeroSpeedResult.Result) {
                _logger.LogWarning(
                    "LoopTrack 系统状态驱动清零失败：SystemState={SystemState}。",
                    currentState);
            }

            var stopResult = await _safeExecutor.ExecuteAsync(
                token => manager.StopAsync(token),
                "LoopTrackManagerHostedService.SystemState.StopAsync",
                false,
                stoppingToken);
            if (!stopResult.Success || !stopResult.Result) {
                _logger.LogError(
                    LoopTrackFaultEventId,
                    "LoopTrack 系统状态驱动停机失败！轨道可能仍在物理运行！SystemState={SystemState} RunStatus={RunStatus}。将在下一轮询周期重试。",
                    currentState,
                    manager.RunStatus);
                // 重置连接状态以便下一轮询周期重新建立连接重试停机；
                // 因 DisconnectAsync 不再强制修改 RunStatus，RunStatus 保持 Running，确保重试不会被早期返回跳过。
                await _safeExecutor.ExecuteAsync(
                    token => manager.DisconnectAsync(token),
                    "LoopTrackManagerHostedService.SystemState.DisconnectAsync.AfterStopFailed",
                    stoppingToken);
                return;
            }

            var disconnectResult = await _safeExecutor.ExecuteAsync(
                token => manager.DisconnectAsync(token),
                "LoopTrackManagerHostedService.SystemState.DisconnectAsync",
                stoppingToken);
            if (!disconnectResult) {
                _logger.LogWarning(
                    "LoopTrack 系统状态驱动断连失败：SystemState={SystemState} ConnectionStatus={ConnectionStatus}。",
                    currentState,
                    manager.ConnectionStatus);
            }
        }

        /// <summary>
        /// 绑定管理器事件。
        /// </summary>
        /// <param name="manager">管理器实例。</param>
        protected virtual void BindEvents(ILoopTrackManager manager) {
            var transport = _options.LeiMaConnection.Transport;
            var slaveAddresses = string.Join(",", _options.LeiMaConnection.SlaveAddresses);
            manager.ConnectionStatusChanged += (_, args) => _safeExecutor.Execute(
                () => _logger.LogInformation(LoopTrackStatusEventId, "LoopTrack连接状态变化 Stage={Stage} Transport={Transport} SlaveAddresses={SlaveAddresses} {OldStatus} -> {NewStatus}，说明={Message}", "LoopTrackManagerHostedService.ConnectionStatusChanged", transport, slaveAddresses, args.OldStatus, args.NewStatus, args.Message),
                "LoopTrackManagerHostedService.ConnectionStatusChanged");

            manager.RunStatusChanged += (_, args) => _safeExecutor.Execute(
                () => _logger.LogInformation(LoopTrackStatusEventId, "LoopTrack运行状态变化 Stage={Stage} Transport={Transport} SlaveAddresses={SlaveAddresses} {OldStatus} -> {NewStatus}，说明={Message}", "LoopTrackManagerHostedService.RunStatusChanged", transport, slaveAddresses, args.OldStatus, args.NewStatus, args.Message),
                "LoopTrackManagerHostedService.RunStatusChanged");

            manager.SpeedChanged += (_, args) => _safeExecutor.Execute(
                () => _logger.LogDebug(LoopTrackStatusEventId, "LoopTrack速度变化 Stage={Stage} Transport={Transport} SlaveAddresses={SlaveAddresses} Target={TargetSpeedMmps}mm/s Real={NewRealTimeSpeedMmps}mm/s", "LoopTrackManagerHostedService.SpeedChanged", transport, slaveAddresses, args.TargetSpeedMmps, args.NewRealTimeSpeedMmps),
                "LoopTrackManagerHostedService.SpeedChanged");

            manager.StabilizationStatusChanged += (_, args) => _safeExecutor.Execute(
                () => _logger.LogInformation(LoopTrackStatusEventId, "LoopTrack稳速状态变化 Stage={Stage} Transport={Transport} SlaveAddresses={SlaveAddresses} {OldStatus} -> {NewStatus}，耗时={StabilizationElapsed}，说明={Message}", "LoopTrackManagerHostedService.StabilizationStatusChanged", transport, slaveAddresses, args.OldStatus, args.NewStatus, args.StabilizationElapsed, args.Message),
                "LoopTrackManagerHostedService.StabilizationStatusChanged");

            manager.Faulted += (_, args) => _safeExecutor.Execute(
                () => _logger.LogError(LoopTrackFaultEventId, args.Exception, "LoopTrack故障事件 OperationId={OperationId} Stage={Stage} Transport={Transport} SlaveAddresses={SlaveAddresses} Operation={Operation} ExceptionType={ExceptionType} ExceptionMessage={ExceptionMessage}", CreateOperationId(), "LoopTrackManagerHostedService.Faulted", transport, slaveAddresses, args.Operation, args.Exception.GetType().Name, args.Exception.Message),
                "LoopTrackManagerHostedService.Faulted");
        }

        /// <summary>
        /// 执行连接并按配置策略重试。
        /// </summary>
        /// <param name="manager">环轨管理器。</param>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>连接是否成功。</returns>
        protected async Task<bool> ConnectWithRetryAsync(ILoopTrackManager manager, CancellationToken stoppingToken) {
            var retry = _options.ConnectRetry;
            var totalAttempts = checked((long)retry.MaxAttempts + 1L);
            Exception? lastException = null;
            return await ExecuteConnectWithRetryPolicyAsync(
                totalAttempts,
                retry.DelayMs,
                retry.MaxDelayMs,
                true,
                "LoopTrack",
                "LoopTrackManagerHostedService.ConnectAsync",
                _options.LeiMaConnection.Transport,
                async token => {
                    return await _safeExecutor.ExecuteAsync(
                        connectToken => manager.ConnectAsync(connectToken),
                        "LoopTrackManagerHostedService.ConnectAsync",
                        false,
                        token,
                        ex => lastException = ex);
                },
                stoppingToken,
                () => lastException);
        }

        /// <summary>
        /// 使用 Polly 策略执行连接重试。
        /// </summary>
        /// <param name="totalAttempts">总尝试次数（含首次）。</param>
        /// <param name="initialDelayMs">初始重试间隔（毫秒）。</param>
        /// <param name="maxDelayMs">重试间隔上限（毫秒）。</param>
        /// <param name="useExponentialBackoff">是否启用指数退避。</param>
        /// <param name="logSubject">日志主体名称。</param>
        /// <param name="stage">日志阶段标识。</param>
        /// <param name="transport">传输模式。</param>
        /// <param name="connectAction">连接执行委托。</param>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <param name="getLastException">获取最近一次异常；返回 null 表示最近一次连接动作未抛出异常（仅返回失败结果）。</param>
        /// <returns>连接是否成功。</returns>
        protected async Task<bool> ExecuteConnectWithRetryPolicyAsync(
            long totalAttempts,
            int initialDelayMs,
            int maxDelayMs,
            bool useExponentialBackoff,
            string logSubject,
            string stage,
            string transport,
            Func<CancellationToken, Task<(bool Success, bool Result)>> connectAction,
            CancellationToken stoppingToken,
            Func<Exception?>? getLastException = null) {
            // 步骤1：创建 Polly 重试策略，并保留重试日志与退避语义。
            var operationId = CreateOperationId();
            var retryCount = (int)(totalAttempts - 1L);
            var executedAttempts = 0L;
            var connectWatch = Stopwatch.StartNew();
            var retryPolicy = Policy
                .HandleResult<(bool Success, bool Result)>(result => !result.Success || !result.Result)
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => TimeSpan.FromMilliseconds(CalculateRetryDelayMs(initialDelayMs, maxDelayMs, retryAttempt, useExponentialBackoff)),
                    (outcome, delay, retryAttempt, _) => {
                        var latestException = outcome.Exception ?? getLastException?.Invoke();
                        var attempt = Math.Min(totalAttempts, (long)retryAttempt);
                        _logger.LogWarning(
                            LoopTrackFaultEventId,
                            "{LogSubject} 连接失败 OperationId={OperationId} Stage={Stage} Attempt={Attempt}/{TotalAttempts} NextDelayMs={DelayMs} Transport={Transport} SlaveAddresses={SlaveAddresses} RetryAttempt={RetryAttempt} ElapsedMs={ElapsedMs} ExceptionType={ExceptionType} ExceptionMessage={ExceptionMessage} 建议=检查从站地址冲突/串口占用/终端电阻/波特率与校验位一致性/RS485端终端电阻。",
                            logSubject,
                            operationId,
                            stage,
                            attempt,
                            totalAttempts,
                            (int)delay.TotalMilliseconds,
                            transport,
                            string.Join(",", _options.LeiMaConnection.SlaveAddresses),
                            retryAttempt,
                            connectWatch.ElapsedMilliseconds,
                            latestException?.GetType().Name ?? "None",
                            latestException?.Message ?? "ResultFailed");
                    });

            try {
                // 步骤2：执行连接策略，危险调用仍通过统一 SafeExecutor 隔离。
                var result = await retryPolicy.ExecuteAsync(async (_, token) => {
                    executedAttempts = checked(executedAttempts + 1L);
                    return await connectAction(token);
                }, new Context(), stoppingToken);

                if (result.Success && result.Result) {
                    _logger.LogInformation(LoopTrackStatusEventId, "{LogSubject} 连接成功 OperationId={OperationId} Stage={Stage} Attempt={Attempt}/{TotalAttempts} Transport={Transport} SlaveAddresses={SlaveAddresses} RetryAttempt={RetryAttempt} ElapsedMs={ElapsedMs} ExceptionType={ExceptionType} ExceptionMessage={ExceptionMessage}。", logSubject, operationId, stage, executedAttempts, totalAttempts, transport, string.Join(",", _options.LeiMaConnection.SlaveAddresses), executedAttempts, connectWatch.ElapsedMilliseconds, "None", "None");
                    return true;
                }
            }
            catch (OperationCanceledException) {
                _logger.LogWarning(LoopTrackFaultEventId, "{LogSubject} 连接流程已取消 OperationId={OperationId} Stage={Stage} Transport={Transport} SlaveAddresses={SlaveAddresses} RetryAttempt={RetryAttempt} ElapsedMs={ElapsedMs} ExceptionType={ExceptionType} ExceptionMessage={ExceptionMessage}。", logSubject, operationId, stage, transport, string.Join(",", _options.LeiMaConnection.SlaveAddresses), executedAttempts, connectWatch.ElapsedMilliseconds, nameof(OperationCanceledException), "连接流程取消");
                return false;
            }

            // 步骤3：策略执行后仍失败，输出终态错误日志。
            var finalException = getLastException?.Invoke();
            _logger.LogError(
                LoopTrackFaultEventId,
                "{LogSubject} 连接失败 OperationId={OperationId} Stage={Stage} 达到最大尝试次数={TotalAttempts} Transport={Transport} SlaveAddresses={SlaveAddresses} RetryAttempt={RetryAttempt} ElapsedMs={ElapsedMs} ExceptionType={ExceptionType} ExceptionMessage={ExceptionMessage}，后台服务退出。建议=检查从站地址冲突/串口占用/终端电阻/波特率与校验位一致性/RS485端终端电阻。",
                logSubject,
                operationId,
                stage,
                totalAttempts,
                transport,
                string.Join(",", _options.LeiMaConnection.SlaveAddresses),
                executedAttempts,
                connectWatch.ElapsedMilliseconds,
                finalException?.GetType().Name ?? "ResultFailed",
                finalException?.Message ?? "连接结果返回失败");
            return false;
        }

        /// <summary>
        /// 计算重试间隔毫秒值。
        /// </summary>
        /// <param name="initialDelayMs">初始间隔。</param>
        /// <param name="maxDelayMs">最大间隔。</param>
        /// <param name="retryAttempt">重试序号（从 1 开始）。</param>
        /// <param name="useExponentialBackoff">是否指数退避。</param>
        /// <returns>退避后的延迟毫秒。</returns>
        protected static int CalculateRetryDelayMs(int initialDelayMs, int maxDelayMs, int retryAttempt, bool useExponentialBackoff) {
            if (!useExponentialBackoff || retryAttempt <= 1) {
                return initialDelayMs;
            }

            var delayMs = (long)initialDelayMs;
            for (var index = 1; index < retryAttempt; index++) {
                delayMs = delayMs >= (long)int.MaxValue / 2L ? int.MaxValue : delayMs * 2L;
                delayMs = Math.Min((long)maxDelayMs, delayMs);
            }

            return (int)delayMs;
        }

        /// <summary>
        /// 获取信号量后执行启停控制，释放信号量后返回，消除轮询中的重复锁逻辑。
        /// </summary>
        /// <param name="manager">环轨管理器。</param>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task ExecuteRunControlWithSemaphoreAsync(ILoopTrackManager manager, CancellationToken stoppingToken) {
            await _runControlSemaphore.WaitAsync(stoppingToken).ConfigureAwait(false);
            try {
                await ApplySystemStateRunControlAsync(manager, stoppingToken).ConfigureAwait(false);
            }
            finally {
                _runControlSemaphore.Release();
            }
        }

        /// <summary>
        /// StateChanged 事件触发的立即停机控制：非阻塞尝试获取信号量后执行停机控制。
        /// 若轮询循环正在持锁，则跳过本次执行（pending 标记已设置，轮询释放锁后会补偿执行）。
        /// </summary>
        /// <param name="manager">环轨管理器。</param>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task TryImmediateStopControlAsync(ILoopTrackManager manager, CancellationToken stoppingToken) {
            if (await _runControlSemaphore.WaitAsync(0).ConfigureAwait(false)) {
                // 成功获取锁：原子清除 pending 标记并执行停机控制。
                Interlocked.Exchange(ref _pendingImmediateStop, 0);
                try {
                    await ApplySystemStateRunControlAsync(manager, stoppingToken).ConfigureAwait(false);
                }
                finally {
                    _runControlSemaphore.Release();
                }
            }
            // 未获取到锁：pending 标记已设置，轮询释放锁后会原子消费并补偿执行。
        }

        /// <summary>
        /// 执行补偿停机与断连。
        /// </summary>
        /// <param name="manager">环轨管理器。</param>
        /// <param name="operationPrefix">操作前缀。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        protected async Task SafeStopAndDisconnectAsync(
            ILoopTrackManager manager,
            string operationPrefix,
            CancellationToken cancellationToken) {
            var zeroSpeedResult = await _safeExecutor.ExecuteAsync(
                token => manager.SetTargetSpeedAsync(0m, token),
                $"{operationPrefix}.SetTargetSpeedZeroAsync",
                false,
                cancellationToken);
            if (!zeroSpeedResult.Success || !zeroSpeedResult.Result) {
                _logger.LogWarning("LoopTrack 退出前清零目标速度失败 Stage={Stage}。", $"{operationPrefix}.SetTargetSpeedZeroAsync");
            }

            var stopResult = await _safeExecutor.ExecuteAsync(
                token => manager.StopAsync(token),
                $"{operationPrefix}.StopAsync",
                false,
                cancellationToken);
            if (!stopResult.Success || !stopResult.Result) {
                _logger.LogWarning("LoopTrack 补偿停机失败 Stage={Stage}。", $"{operationPrefix}.StopAsync");
            }

            var disconnectResult = await _safeExecutor.ExecuteAsync(
                token => manager.DisconnectAsync(token),
                $"{operationPrefix}.DisconnectAsync",
                cancellationToken);
            if (!disconnectResult) {
                _logger.LogWarning("LoopTrack 补偿断连失败 Stage={Stage}。", $"{operationPrefix}.DisconnectAsync");
            }
        }

        /// <summary>
        /// 安全释放管理器资源。
        /// </summary>
        /// <param name="manager">环轨管理器。</param>
        /// <returns>异步任务。</returns>
        protected async Task ReleaseManagerSafelyAsync(ILoopTrackManager manager) {
            var disposeResult = await _safeExecutor.ExecuteAsync(
                () => manager.DisposeAsync().AsTask(),
                "LoopTrackManagerHostedService.DisposeAsync");
            if (!disposeResult) {
                _logger.LogWarning("LoopTrack 管理器释放失败 Stage={Stage}。", "LoopTrackManagerHostedService.DisposeAsync");
            }
        }

        /// <summary>
        /// 校验服务配置合法性。
        /// </summary>
        /// <param name="options">服务配置。</param>
        /// <param name="validationMessage">校验消息。</param>
        /// <returns>配置是否有效。</returns>
        protected static bool TryValidateOptions(LoopTrackServiceOptions options, out string validationMessage) {
            // 步骤1：校验基础标识，避免无效名称导致定位困难。
            if (string.IsNullOrWhiteSpace(options.TrackName)) {
                validationMessage = "TrackName 不能为空。";
                return false;
            }

            // 步骤2：校验连接参数，确保 Modbus 链路具备可连接前提。
            var transport = options.LeiMaConnection.Transport;
            if (string.IsNullOrWhiteSpace(transport)) {
                validationMessage = "LeiMaConnection.Transport 不能为空。";
                return false;
            }

            var isTcpGateway = string.Equals(transport, LoopTrackLeiMaTransportModes.TcpGateway, StringComparison.OrdinalIgnoreCase);
            var isSerialRtu = string.Equals(transport, LoopTrackLeiMaTransportModes.SerialRtu, StringComparison.OrdinalIgnoreCase);
            if (!isTcpGateway && !isSerialRtu) {
                validationMessage = $"LeiMaConnection.Transport 不支持：{transport}。";
                return false;
            }

            if (isTcpGateway && string.IsNullOrWhiteSpace(options.LeiMaConnection.RemoteHost)) {
                validationMessage = "Transport=TcpGateway 时 LeiMaConnection.RemoteHost 不能为空。";
                return false;
            }

            if (isSerialRtu) {
                var serial = options.LeiMaConnection.SerialRtu;
                if (string.IsNullOrWhiteSpace(serial.PortName)) {
                    validationMessage = "Transport=SerialRtu 时 LeiMaConnection.SerialRtu.PortName 不能为空。";
                    return false;
                }

                if (serial.BaudRate <= 0) {
                    validationMessage = "Transport=SerialRtu 时 LeiMaConnection.SerialRtu.BaudRate 必须大于 0。";
                    return false;
                }

                if (serial.DataBits is < 5 or > 8) {
                    validationMessage = "Transport=SerialRtu 时 LeiMaConnection.SerialRtu.DataBits 必须在 5~8 范围内。";
                    return false;
                }

                if (!Enum.IsDefined(typeof(System.IO.Ports.Parity), serial.Parity)) {
                    validationMessage = "Transport=SerialRtu 时 LeiMaConnection.SerialRtu.Parity 非法。";
                    return false;
                }

                if (!Enum.IsDefined(typeof(System.IO.Ports.StopBits), serial.StopBits) || serial.StopBits == System.IO.Ports.StopBits.None) {
                    validationMessage = "Transport=SerialRtu 时 LeiMaConnection.SerialRtu.StopBits 非法。";
                    return false;
                }
            }

            var slaveAddresses = options.LeiMaConnection.SlaveAddresses;
            if (slaveAddresses is null || slaveAddresses.Count == 0) {
                validationMessage = "LeiMaConnection.SlaveAddresses 至少需要配置一个从站地址。";
                return false;
            }

            if (slaveAddresses.Any(address => address is < 1 or > 247)) {
                validationMessage = "LeiMaConnection.SlaveAddresses 的每个地址必须在 1~247 范围内。";
                return false;
            }

            var uniqueSlaveAddressCount = slaveAddresses.ToHashSet().Count;
            if (uniqueSlaveAddressCount != slaveAddresses.Count) {
                validationMessage = "LeiMaConnection.SlaveAddresses 不能包含重复地址。";
                return false;
            }

            if (options.LeiMaConnection.TimeoutMs <= 0) {
                validationMessage = "LeiMaConnection.TimeoutMs 必须大于 0。";
                return false;
            }

            if (options.LeiMaConnection.RetryCount < 0) {
                validationMessage = "LeiMaConnection.RetryCount 不能小于 0。";
                return false;
            }

            if (options.LeiMaConnection.MaxOutputHz <= 0m) {
                validationMessage = "LeiMaConnection.MaxOutputHz 必须大于 0。";
                return false;
            }

            if (!Enum.IsDefined(options.LeiMaConnection.SpeedAggregateStrategy) || options.LeiMaConnection.SpeedAggregateStrategy == 0) {
                validationMessage = "LeiMaConnection.SpeedAggregateStrategy 仅支持 Min/Avg/Median。";
                return false;
            }

            // 步骤3：校验设速链路参数，保障 P3.10 映射输入边界有效。
            if (options.LeiMaConnection.MaxTorqueRawUnit == 0) {
                validationMessage = "LeiMaConnection.MaxTorqueRawUnit 不能为 0。";
                return false;
            }

            if (options.LeiMaConnection.TorqueSetpointWriteIntervalMs <= 0) {
                validationMessage = "LeiMaConnection.TorqueSetpointWriteIntervalMs 必须大于 0。";
                return false;
            }

            if (options.TargetSpeedMmps < 0m) {
                validationMessage = "TargetSpeedMmps 不能小于 0。";
                return false;
            }

            if (options.PollingIntervalMs <= 0) {
                validationMessage = "PollingIntervalMs 必须大于 0。";
                return false;
            }
            if (options.StabilizedToleranceMmps < 0m) {
                validationMessage = "StabilizedToleranceMmps 不能小于 0。";
                return false;
            }

            if (options.StabilizedWindowMs <= 0) {
                validationMessage = "StabilizedWindowMs 必须大于 0。";
                return false;
            }
            if (options.Pid.Kp < 0m || options.Pid.Ki < 0m || options.Pid.Kd < 0m) {
                validationMessage = "Pid.Kp、Pid.Ki、Pid.Kd 不能为负数。";
                return false;
            }

            if (options.Pid.OutputMinRaw > options.Pid.OutputMaxRaw) {
                validationMessage = "Pid.OutputMinRaw 不能大于 Pid.OutputMaxRaw。";
                return false;
            }

            if (options.Pid.IntegralMin > options.Pid.IntegralMax) {
                validationMessage = "Pid.IntegralMin 不能大于 Pid.IntegralMax。";
                return false;
            }

            if (options.Pid.DerivativeFilterAlpha < 0m || options.Pid.DerivativeFilterAlpha > 1m) {
                validationMessage = "Pid.DerivativeFilterAlpha 必须在 0~1 范围内。";
                return false;
            }

            // 步骤4：校验重试与日志频率配置，避免重试失控或调试输出异常。
            if (options.ConnectRetry.MaxAttempts < 0) {
                validationMessage = "ConnectRetry.MaxAttempts 不能小于 0。";
                return false;
            }

            if (options.ConnectRetry.MaxAttempts == int.MaxValue) {
                validationMessage = "ConnectRetry.MaxAttempts 不能为 int.MaxValue。";
                return false;
            }

            if (options.ConnectRetry.DelayMs <= 0) {
                validationMessage = "ConnectRetry.DelayMs 必须大于 0。";
                return false;
            }

            if (options.ConnectRetry.MaxDelayMs < options.ConnectRetry.DelayMs) {
                validationMessage = "ConnectRetry.MaxDelayMs 必须大于等于 ConnectRetry.DelayMs。";
                return false;
            }

            if (options.Logging.DebugStatusIntervalMs <= 0) {
                validationMessage = "Logging.DebugStatusIntervalMs 必须大于 0。";
                return false;
            }

            if (options.Logging.InfoStatusIntervalMs <= 0) {
                validationMessage = "Logging.InfoStatusIntervalMs 必须大于 0。";
                return false;
            }

            if (options.Logging.RealtimeSpeedLogIntervalMs <= 0) {
                validationMessage = "Logging.RealtimeSpeedLogIntervalMs 必须大于 0。";
                return false;
            }

            if (options.Logging.PidTuningLogIntervalMs <= 0) {
                validationMessage = "Logging.PidTuningLogIntervalMs 必须大于 0。";
                return false;
            }

            if (options.Logging.UnstableDeviationThresholdMmps < 0m) {
                validationMessage = "Logging.UnstableDeviationThresholdMmps 不能小于 0。";
                return false;
            }

            if (options.Logging.UnstableDurationMs <= 0) {
                validationMessage = "Logging.UnstableDurationMs 必须大于 0。";
                return false;
            }

            validationMessage = string.Empty;
            return true;
        }

        /// <summary>
        /// 判断当前状态是否允许输出实时速度日志。
        /// </summary>
        /// <param name="systemState">系统状态快照。</param>
        /// <param name="runStatus">环轨运行状态快照。</param>
        /// <returns>允许输出返回 true，不允许输出返回 false。</returns>
        private static bool ShouldWriteRealtimeSpeedLog(SystemState systemState, LoopTrackRunStatus runStatus) {
            if (systemState == SystemState.EmergencyStop) {
                return false;
            }

            return runStatus switch {
                LoopTrackRunStatus.Stopped => false,
                LoopTrackRunStatus.Faulted => false,
                _ => true
            };
        }

        /// <summary>
        /// 释放配置热更新订阅资源。
        /// </summary>
        public override void Dispose() {
            _optionsChangedRegistration.Dispose();
            _runControlSemaphore.Dispose();
            base.Dispose();
        }

        /// <summary>
        /// 创建短格式操作编号。
        /// </summary>
        /// <returns>操作编号。</returns>
        protected static string CreateOperationId() {
            var hostOperationId = OperationIdFactory.CreateShortOperationId();
            return hostOperationId;
        }
    }
}
