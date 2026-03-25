using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using System.Diagnostics;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Utilities.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa;

namespace Zeye.NarrowBeltSorter.Host.Services {
    /// <summary>
    /// 环形轨道管理后台服务。
    /// </summary>
    public class LoopTrackManagerService : BackgroundService {
        private readonly ILogger<LoopTrackManagerService> _logger;
        private readonly SafeExecutor _safeExecutor;
        private readonly LoopTrackServiceOptions _options;
        /// <summary>
        /// 当前服务持有的环轨管理器实例；受保护可供派生类访问，生命周期释放与置空由服务停止流程统一控制，禁止跨线程替换。
        /// </summary>
        protected ILoopTrackManager? _manager;
        private int _stopRequestedFlag;

        /// <summary>
        /// 主服务日志组件。
        /// </summary>
        protected ILogger<LoopTrackManagerService> Logger => _logger;

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
        /// <param name="options">服务配置。</param>
        public LoopTrackManagerService(
            ILogger<LoopTrackManagerService> logger,
            SafeExecutor safeExecutor,
            IOptions<LoopTrackServiceOptions> options) {
            _logger = logger;
            _safeExecutor = safeExecutor;
            _options = options.Value;
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
            _manager = manager;
            BindEvents(manager);

            _logger.LogInformation(
                "LoopTrack 运行模式=Main Track={TrackName} Transport={Transport} Host={RemoteHost} SerialPort={SerialPort} Slave={SlaveAddress} TimeoutMs={TimeoutMs} RetryCount={RetryCount} PollingIntervalMs={PollingIntervalMs}",
                _options.TrackName,
                _options.LeiMaConnection.Transport,
                _options.LeiMaConnection.RemoteHost,
                _options.LeiMaConnection.SerialRtu.PortName,
                _options.LeiMaConnection.SlaveAddress,
                _options.LeiMaConnection.TimeoutMs,
                _options.LeiMaConnection.RetryCount,
                _options.PollingIntervalMs);

            // 步骤2：执行配置化连接重试，连接失败时退出并释放资源。
            var connected = await ConnectWithRetryAsync(manager, stoppingToken);
            if (!connected) {
                await ReleaseManagerSafelyAsync(manager);
                _manager = null;
                return;
            }

            // 步骤3：AutoStart 流程执行 Start -> SetTargetSpeed，失败时执行补偿停机与断连。
            if (_options.AutoStart) {
                var autoStartSuccess = await TryAutoStartAsync(manager, stoppingToken);
                if (!autoStartSuccess) {
                    await SafeStopAndDisconnectAsync(manager, "LoopTrackManagerService.AutoStartCompensation", stoppingToken);
                    await ReleaseManagerSafelyAsync(manager);
                    _manager = null;
                    return;
                }
            }

            // 步骤4：持续状态监测与结构化日志输出，Info 常态输出，Debug 受配置频率控制。
            await MonitorStatusLoopAsync(manager, pollingInterval, infoStatusInterval, debugStatusInterval, stoppingToken);
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
                await SafeStopAndDisconnectAsync(manager, "LoopTrackManagerService.Stop", cancellationToken);

                // 步骤3：释放资源，保证后台任务结束。
                await ReleaseManagerSafelyAsync(manager);
                _manager = null;
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
                SlaveAddress = connection.SlaveAddress,
                TimeoutMilliseconds = connection.TimeoutMs,
                RetryCount = connection.RetryCount
            };

            var adapter = CreateAdapter(connection);

            return new LeiMaLoopTrackManager(
                trackName: _options.TrackName,
                modbusClient: adapter,
                safeExecutor: _safeExecutor,
                connectionOptions: connectionOptions,
                pidOptions: _options.Pid,
                maxOutputHz: connection.MaxOutputHz,
                maxTorqueRawUnit: connection.MaxTorqueRawUnit,
                pollingInterval: pollingInterval,
                torqueSetpointWriteInterval: TimeSpan.FromMilliseconds(connection.TorqueSetpointWriteIntervalMs));
        }

        /// <summary>
        /// 按传输模式创建 Modbus 适配器。
        /// </summary>
        /// <param name="connection">连接配置。</param>
        /// <returns>Modbus 适配器。</returns>
        protected virtual ILeiMaModbusClientAdapter CreateAdapter(LoopTrackLeiMaConnectionOptions connection) {
            if (string.Equals(connection.Transport, LoopTrackLeiMaTransportModes.SerialRtu, StringComparison.OrdinalIgnoreCase)) {
                var serial = connection.SerialRtu;
                return CreateSerialRtuAdapter(serial, connection);
            }

            return CreateTcpGatewayAdapter(connection);
        }

        /// <summary>
        /// 创建 TCP 网关适配器。
        /// </summary>
        /// <param name="connection">连接配置。</param>
        /// <returns>适配器实例。</returns>
        protected virtual ILeiMaModbusClientAdapter CreateTcpGatewayAdapter(LoopTrackLeiMaConnectionOptions connection) {
            return new LeiMaModbusClientAdapter(
                connection.RemoteHost,
                connection.SlaveAddress,
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
            LoopTrackLeiMaConnectionOptions connection) {
            return new LeiMaModbusClientAdapter(
                serial.PortName,
                serial.BaudRate,
                serial.Parity,
                serial.DataBits,
                serial.StopBits,
                connection.SlaveAddress,
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
            var started = await _safeExecutor.ExecuteAsync(
                token => manager.StartAsync(token),
                "LoopTrackManagerService.StartAsync",
                false,
                stoppingToken);

            if (!started.Success || !started.Result) {
                _logger.LogWarning("LoopTrack 自动启动失败 Stage={Stage}，已触发补偿链路。", "StartAsync");
                return false;
            }

            var setSpeedResult = await _safeExecutor.ExecuteAsync(
                token => manager.SetTargetSpeedAsync(_options.TargetSpeedMmps, token),
                "LoopTrackManagerService.SetTargetSpeedAsync",
                false,
                stoppingToken);

            if (!setSpeedResult.Success || !setSpeedResult.Result) {
                _logger.LogWarning("LoopTrack 自动设速失败 Stage={Stage} TargetSpeedMmps={TargetSpeedMmps}，已触发补偿链路。", "SetTargetSpeedAsync", _options.TargetSpeedMmps);
                return false;
            }

            _logger.LogInformation("LoopTrack 自动启动完成，目标速度={TargetSpeedMmps}mm/s。", _options.TargetSpeedMmps);
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
            var infoIntervalMs = (long)infoStatusInterval.TotalMilliseconds;
            var debugIntervalMs = (long)debugStatusInterval.TotalMilliseconds;
            var realtimeSpeedLogIntervalMs = (long)_options.Logging.RealtimeSpeedLogIntervalMs;
            var pidTuningLogIntervalMs = (long)_options.Logging.PidTuningLogIntervalMs;
            var nextInfoLogElapsedMs = infoIntervalMs;
            var nextDebugLogElapsedMs = debugIntervalMs;
            var nextRealtimeSpeedLogElapsedMs = realtimeSpeedLogIntervalMs;
            var nextPidTuningLogElapsedMs = pidTuningLogIntervalMs;
            var enableVerboseStatus = _options.Logging.EnableVerboseStatus;
            var enableRealtimeSpeedLog = _options.Logging.EnableRealtimeSpeedLog;
            var enablePidTuningLog = _options.Logging.EnablePidTuningLog;
            var instabilityThreshold = _options.Logging.UnstableDeviationThresholdMmps;
            var instabilityDurationMs = _options.Logging.UnstableDurationMs;
            var pollingIntervalMs = (long)pollingInterval.TotalMilliseconds;
            var unstableElapsedMs = 0L;
            var unstableLogged = false;
            var invalidPollingIntervalLogged = false;

            try {
                while (await timer.WaitForNextTickAsync(stoppingToken)) {
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
                                        "LoopTrack 失稳告警 Name={TrackName} Target={TargetSpeedMmps}mm/s RealTime={RealTimeSpeedMmps}mm/s Deviation={SpeedDeviationMmps}mm/s Threshold={ThresholdMmps}mm/s DurationMs={DurationMs}",
                                        trackName,
                                        targetSpeedMmps,
                                        realTimeSpeedMmps,
                                        speedDeviationMmps,
                                        instabilityThreshold,
                                        unstableElapsedMs);
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
                                    "LoopTrack状态 Name={TrackName} Conn={ConnectionStatus} Run={RunStatus} Stabilization={StabilizationStatus} StabilizationElapsed={StabilizationElapsed} Target={TargetSpeedMmps}mm/s RealTime={RealTimeSpeedMmps}mm/s Deviation={SpeedDeviationMmps}mm/s",
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
                            if (enableRealtimeSpeedLog && statusWatch.ElapsedMilliseconds >= nextRealtimeSpeedLogElapsedMs) {
                                _logger.LogInformation(
                                    "LoopTrack实时速度 Name={TrackName} Target={TargetSpeedMmps}mm/s RealTime={RealTimeSpeedMmps}mm/s Deviation={SpeedDeviationMmps}mm/s Run={RunStatus} Stabilization={StabilizationStatus}",
                                    trackName,
                                    targetSpeedMmps,
                                    realTimeSpeedMmps,
                                    speedDeviationMmps,
                                    runStatus,
                                    stabilizationStatus);

                                nextRealtimeSpeedLogElapsedMs = statusWatch.ElapsedMilliseconds + realtimeSpeedLogIntervalMs;
                            }

                            // 步骤5：按配置频率输出 PID 调参日志。
                            if (enablePidTuningLog && statusWatch.ElapsedMilliseconds >= nextPidTuningLogElapsedMs && manager.PidLastUpdatedAt.HasValue) {
                                _logger.LogDebug(
                                    "LoopTrack调参 Name={TrackName} P={ProportionalHz}Hz I={IntegralHz}Hz D={DerivativeHz}Hz Error={ErrorMmps}mm/s Command={CommandHz}Hz Unclamped={UnclampedHz}Hz Clamped={OutputClamped} UpdatedAt={UpdatedAt}",
                                    trackName,
                                    manager.PidLastProportionalHz,
                                    manager.PidLastIntegralHz,
                                    manager.PidLastDerivativeHz,
                                    manager.PidLastErrorMmps,
                                    manager.PidLastCommandHz,
                                    manager.PidLastUnclampedHz,
                                    manager.PidLastOutputClamped,
                                    manager.PidLastUpdatedAt);

                                nextPidTuningLogElapsedMs = statusWatch.ElapsedMilliseconds + pidTuningLogIntervalMs;
                            }
                        },
                        "LoopTrackManagerService.MonitorStatusLoop");
                }
            }
            catch (OperationCanceledException) {
                _logger.LogInformation("LoopTrack 后台服务收到停止信号。");
            }
        }

        /// <summary>
        /// 绑定管理器事件。
        /// </summary>
        /// <param name="manager">管理器实例。</param>
        protected virtual void BindEvents(ILoopTrackManager manager) {
            manager.ConnectionStatusChanged += (_, args) => _safeExecutor.Execute(
                () => _logger.LogInformation("LoopTrack连接状态变化 {OldStatus} -> {NewStatus}，说明={Message}", args.OldStatus, args.NewStatus, args.Message),
                "LoopTrackManagerService.ConnectionStatusChanged");

            manager.RunStatusChanged += (_, args) => _safeExecutor.Execute(
                () => _logger.LogInformation("LoopTrack运行状态变化 {OldStatus} -> {NewStatus}，说明={Message}", args.OldStatus, args.NewStatus, args.Message),
                "LoopTrackManagerService.RunStatusChanged");

            manager.SpeedChanged += (_, args) => _safeExecutor.Execute(
                () => _logger.LogInformation("LoopTrack速度变化 Target={TargetSpeedMmps}mm/s Real={NewRealTimeSpeedMmps}mm/s", args.TargetSpeedMmps, args.NewRealTimeSpeedMmps),
                "LoopTrackManagerService.SpeedChanged");

            manager.StabilizationStatusChanged += (_, args) => _safeExecutor.Execute(
                () => _logger.LogInformation("LoopTrack稳速状态变化 {OldStatus} -> {NewStatus}，耗时={StabilizationElapsed}，说明={Message}", args.OldStatus, args.NewStatus, args.StabilizationElapsed, args.Message),
                "LoopTrackManagerService.StabilizationStatusChanged");

            manager.Faulted += (_, args) => _safeExecutor.Execute(
                () => _logger.LogError(args.Exception, "LoopTrack故障事件 Operation={Operation}", args.Operation),
                "LoopTrackManagerService.Faulted");
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
            return await ExecuteConnectWithRetryPolicyAsync(
                totalAttempts,
                retry.DelayMs,
                retry.MaxDelayMs,
                true,
                "LoopTrack",
                "LoopTrackManagerService.ConnectAsync",
                _options.LeiMaConnection.Transport,
                token => _safeExecutor.ExecuteAsync(
                    connectToken => manager.ConnectAsync(connectToken),
                    "LoopTrackManagerService.ConnectAsync",
                    false,
                    token),
                stoppingToken);
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
            CancellationToken stoppingToken) {
            // 步骤1：创建 Polly 重试策略，并保留重试日志与退避语义。
            var retryCount = (int)Math.Max(0L, totalAttempts - 1L);
            var executedAttempts = 0L;
            AsyncRetryPolicy<(bool Success, bool Result)> retryPolicy = Policy
                .HandleResult<(bool Success, bool Result)>(result => !result.Success || !result.Result)
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => TimeSpan.FromMilliseconds(CalculateRetryDelayMs(initialDelayMs, maxDelayMs, retryAttempt, useExponentialBackoff)),
                    (outcome, delay, retryAttempt, _) => {
                        var attempt = Math.Min(totalAttempts, (long)retryAttempt);
                        _logger.LogWarning(
                            "{LogSubject} 连接失败 Stage={Stage} Attempt={Attempt}/{TotalAttempts} NextDelayMs={DelayMs} Transport={Transport}。",
                            logSubject,
                            stage,
                            attempt,
                            totalAttempts,
                            (int)delay.TotalMilliseconds,
                            transport);
                    });

            try {
                // 步骤2：执行连接策略，危险调用仍通过统一 SafeExecutor 隔离。
                var result = await retryPolicy.ExecuteAsync(async (_, token) => {
                    executedAttempts = checked(executedAttempts + 1L);
                    return await connectAction(token);
                }, new Context(), stoppingToken);

                if (result.Success && result.Result) {
                    _logger.LogInformation("{LogSubject} 连接成功 Attempt={Attempt}/{TotalAttempts} Transport={Transport}。", logSubject, executedAttempts, totalAttempts, transport);
                    return true;
                }
            }
            catch (OperationCanceledException) {
                _logger.LogWarning("{LogSubject} 连接流程已取消 Stage={Stage} Transport={Transport}。", logSubject, stage, transport);
                return false;
            }

            // 步骤3：策略执行后仍失败，输出终态错误日志。
            _logger.LogError(
                "{LogSubject} 连接失败 Stage={Stage} 达到最大尝试次数={TotalAttempts} Transport={Transport}，后台服务退出。",
                logSubject,
                stage,
                totalAttempts,
                transport);
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
                "LoopTrackManagerService.DisposeAsync");
            if (!disposeResult) {
                _logger.LogWarning("LoopTrack 管理器释放失败 Stage={Stage}。", "LoopTrackManagerService.DisposeAsync");
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

            if (options.LeiMaConnection.SlaveAddress < 1 || options.LeiMaConnection.SlaveAddress > 247) {
                validationMessage = "LeiMaConnection.SlaveAddress（Modbus RTU 语义）必须在 1~247 范围内。";
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

            if (options.Pid.Kp < 0m || options.Pid.Ki < 0m || options.Pid.Kd < 0m) {
                validationMessage = "Pid.Kp、Pid.Ki、Pid.Kd 不能为负数。";
                return false;
            }

            if (options.Pid.OutputMinHz > options.Pid.OutputMaxHz) {
                validationMessage = "Pid.OutputMinHz 不能大于 Pid.OutputMaxHz。";
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

    }
}
