using Microsoft.Extensions.Options;
using System.Diagnostics;
using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa;
using Zeye.NarrowBeltSorter.Host.Options.LoopTrack;

namespace Zeye.NarrowBeltSorter.Host.Servers {
    /// <summary>
    /// 环形轨道管理后台服务。
    /// </summary>
    public sealed class LoopTrackManagerService : BackgroundService {
        private readonly ILogger<LoopTrackManagerService> _logger;
        private readonly SafeExecutor _safeExecutor;
        private readonly LoopTrackServiceOptions _options;
        private LeiMaLoopTrackManager? _manager;
        private int _stopRequestedFlag;

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
            var debugStatusInterval = TimeSpan.FromMilliseconds(_options.Logging.DebugStatusIntervalMs);
            var manager = CreateManager(pollingInterval);
            _manager = manager;
            BindEvents(manager);

            _logger.LogInformation(
                "LoopTrack 主服务启动 Track={TrackName} Host={RemoteHost} Slave={SlaveAddress} TimeoutMs={TimeoutMs} RetryCount={RetryCount} PollingIntervalMs={PollingIntervalMs}",
                _options.TrackName,
                _options.LeiMaConnection.RemoteHost,
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
            await MonitorStatusLoopAsync(manager, pollingInterval, debugStatusInterval, stoppingToken);
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

            var manager = _manager;
            if (manager is not null) {
                // 步骤1：优先停机并断连，失败不中断后续释放。
                await SafeStopAndDisconnectAsync(manager, "LoopTrackManagerService.Stop", cancellationToken);

                // 步骤2：释放资源，保证后台任务结束。
                await ReleaseManagerSafelyAsync(manager);
                _manager = null;
            }

            await base.StopAsync(cancellationToken);
        }

        /// <summary>
        /// 构造雷码环轨管理器实例。
        /// </summary>
        /// <param name="pollingInterval">轮询周期。</param>
        /// <returns>管理器实例。</returns>
        private LeiMaLoopTrackManager CreateManager(TimeSpan pollingInterval) {
            var connection = _options.LeiMaConnection;
            var connectionOptions = new LoopTrackConnectionOptions {
                SlaveAddress = connection.SlaveAddress,
                TimeoutMilliseconds = connection.TimeoutMs,
                RetryCount = connection.RetryCount
            };

            var adapter = new LeiMaModbusClientAdapter(
                connection.RemoteHost,
                connection.SlaveAddress,
                connection.TimeoutMs,
                connection.RetryCount);

            return new LeiMaLoopTrackManager(
                trackName: _options.TrackName,
                modbusClient: adapter,
                safeExecutor: _safeExecutor,
                connectionOptions: connectionOptions,
                pidOptions: _options.Pid,
                maxOutputHz: connection.MaxOutputHz,
                maxTorqueRawUnit: connection.MaxTorqueRawUnit,
                pollingInterval: pollingInterval);
        }

        /// <summary>
        /// 执行自动启动流程。
        /// </summary>
        /// <param name="manager">环轨管理器。</param>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>自动启动是否成功。</returns>
        private async Task<bool> TryAutoStartAsync(LeiMaLoopTrackManager manager, CancellationToken stoppingToken) {
            var started = await _safeExecutor.ExecuteAsync(
                token => manager.StartAsync(token),
                "LoopTrackManagerService.StartAsync",
                false,
                stoppingToken);

            if (!started.Success || !started.Result) {
                _logger.LogWarning("LoopTrack 自动启动失败，已触发补偿链路。");
                return false;
            }

            var setSpeedResult = await _safeExecutor.ExecuteAsync(
                token => manager.SetTargetSpeedAsync(_options.TargetSpeedMmps, token),
                "LoopTrackManagerService.SetTargetSpeedAsync",
                false,
                stoppingToken);

            if (!setSpeedResult.Success || !setSpeedResult.Result) {
                _logger.LogWarning("LoopTrack 自动设速失败，目标速度={TargetSpeedMmps}mm/s，已触发补偿链路。", _options.TargetSpeedMmps);
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
            LeiMaLoopTrackManager manager,
            TimeSpan pollingInterval,
            TimeSpan debugStatusInterval,
            CancellationToken stoppingToken) {
            using var timer = new PeriodicTimer(pollingInterval);
            var statusWatch = Stopwatch.StartNew();
            var debugIntervalMs = (long)debugStatusInterval.TotalMilliseconds;
            var nextDebugLogElapsedMs = debugIntervalMs;
            var enableVerboseStatus = _options.Logging.EnableVerboseStatus;

            try {
                while (await timer.WaitForNextTickAsync(stoppingToken)) {
                    _safeExecutor.Execute(
                        () => {
                            var trackName = manager.TrackName;
                            var connectionStatus = manager.ConnectionStatus;
                            var runStatus = manager.RunStatus;
                            var stabilizationStatus = manager.StabilizationStatus;
                            var targetSpeedMmps = manager.TargetSpeedMmps;
                            var realTimeSpeedMmps = manager.RealTimeSpeedMmps;

                            _logger.LogInformation(
                                "LoopTrack状态 Name={TrackName} Conn={ConnectionStatus} Run={RunStatus} Stabilization={StabilizationStatus} Target={TargetSpeedMmps}mm/s RealTime={RealTimeSpeedMmps}mm/s",
                                trackName,
                                connectionStatus,
                                runStatus,
                                stabilizationStatus,
                                targetSpeedMmps,
                                realTimeSpeedMmps);

                            if (enableVerboseStatus && statusWatch.ElapsedMilliseconds >= nextDebugLogElapsedMs) {
                                _logger.LogDebug(
                                    "LoopTrack调试状态 Name={TrackName} Conn={ConnectionStatus} Run={RunStatus} Stabilization={StabilizationStatus} Target={TargetSpeedMmps}mm/s RealTime={RealTimeSpeedMmps}mm/s",
                                    trackName,
                                    connectionStatus,
                                    runStatus,
                                    stabilizationStatus,
                                    targetSpeedMmps,
                                    realTimeSpeedMmps);

                                nextDebugLogElapsedMs = statusWatch.ElapsedMilliseconds + debugIntervalMs;
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
        private void BindEvents(LeiMaLoopTrackManager manager) {
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
        private async Task<bool> ConnectWithRetryAsync(LeiMaLoopTrackManager manager, CancellationToken stoppingToken) {
            var retry = _options.ConnectRetry;
            var attempt = 0;
            var totalAttempts = retry.MaxAttempts + 1;
            var delayMs = retry.DelayMs;
            var maxDelayMs = retry.MaxDelayMs;

            while (!stoppingToken.IsCancellationRequested && attempt < totalAttempts) {
                attempt++;
                var connected = await _safeExecutor.ExecuteAsync(
                    token => manager.ConnectAsync(token),
                    "LoopTrackManagerService.ConnectAsync",
                    false,
                    stoppingToken);

                if (connected.Success && connected.Result) {
                    _logger.LogInformation("LoopTrack 连接成功，尝试次数={Attempt}/{TotalAttempts}。", attempt, totalAttempts);
                    return true;
                }

                if (attempt >= totalAttempts) {
                    break;
                }

                _logger.LogWarning(
                    "LoopTrack 连接失败，{DelayMs}ms 后重试。当前尝试={Attempt}/{TotalAttempts}",
                    delayMs,
                    attempt,
                    totalAttempts);

                try {
                    await Task.Delay(TimeSpan.FromMilliseconds(delayMs), stoppingToken);
                }
                catch (OperationCanceledException) {
                    break;
                }

                var doubledDelayMs = (long)delayMs * 2L;
                var boundedDelayMs = Math.Min((long)maxDelayMs, Math.Min((long)int.MaxValue, doubledDelayMs));
                delayMs = (int)boundedDelayMs;
            }

            _logger.LogError("LoopTrack 连接失败，达到最大尝试次数={TotalAttempts}，后台服务退出。", totalAttempts);
            return false;
        }

        /// <summary>
        /// 执行补偿停机与断连。
        /// </summary>
        /// <param name="manager">环轨管理器。</param>
        /// <param name="operationPrefix">操作前缀。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task SafeStopAndDisconnectAsync(
            LeiMaLoopTrackManager manager,
            string operationPrefix,
            CancellationToken cancellationToken) {
            await _safeExecutor.ExecuteAsync(
                token => manager.StopAsync(token),
                $"{operationPrefix}.StopAsync",
                false,
                cancellationToken);

            await _safeExecutor.ExecuteAsync(
                token => manager.DisconnectAsync(token),
                $"{operationPrefix}.DisconnectAsync",
                cancellationToken);
        }

        /// <summary>
        /// 安全释放管理器资源。
        /// </summary>
        /// <param name="manager">环轨管理器。</param>
        /// <returns>异步任务。</returns>
        private async Task ReleaseManagerSafelyAsync(LeiMaLoopTrackManager manager) {
            await _safeExecutor.ExecuteAsync(
                () => manager.DisposeAsync().AsTask(),
                "LoopTrackManagerService.DisposeAsync");
        }

        /// <summary>
        /// 校验服务配置合法性。
        /// </summary>
        /// <param name="options">服务配置。</param>
        /// <param name="validationMessage">校验消息。</param>
        /// <returns>配置是否有效。</returns>
        private static bool TryValidateOptions(LoopTrackServiceOptions options, out string validationMessage) {
            // 步骤1：校验基础标识，避免无效名称导致定位困难。
            if (string.IsNullOrWhiteSpace(options.TrackName)) {
                validationMessage = "TrackName 不能为空。";
                return false;
            }

            // 步骤2：校验连接参数，确保 Modbus 链路具备可连接前提。
            if (string.IsNullOrWhiteSpace(options.LeiMaConnection.RemoteHost)) {
                validationMessage = "LeiMaConnection.RemoteHost 不能为空。";
                return false;
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

            // 步骤4：校验重试与日志频率配置，避免重试失控或调试输出异常。
            if (options.ConnectRetry.MaxAttempts < 0) {
                validationMessage = "ConnectRetry.MaxAttempts 不能小于 0。";
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

            validationMessage = string.Empty;
            return true;
        }

    }
}
