using Microsoft.Extensions.Options;
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
        private readonly TimeSpan _connectRetryDelay = TimeSpan.FromSeconds(3);
        private LeiMaLoopTrackManager? _manager;

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

            var pollingIntervalMs = _options.PollingIntervalMs;
            if (pollingIntervalMs <= 0) {
                _logger.LogWarning("LoopTrack.PollingIntervalMs 配置无效，回退为 300ms。");
                pollingIntervalMs = 300;
            }

            var connection = _options.LeiMaConnection;
            if (!TryValidateOptions(_options, out var validationMessage)) {
                _logger.LogError("LoopTrack 配置无效，后台服务退出。原因：{ValidationMessage}", validationMessage);
                return;
            }

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

            _manager = new LeiMaLoopTrackManager(
                trackName: _options.TrackName,
                modbusClient: adapter,
                safeExecutor: _safeExecutor,
                connectionOptions: connectionOptions,
                pidOptions: _options.Pid,
                maxOutputHz: connection.MaxOutputHz,
                maxTorqueRawUnit: connection.MaxTorqueRawUnit,
                pollingInterval: TimeSpan.FromMilliseconds(pollingIntervalMs));

            BindEvents(_manager);
            var manager = _manager;

            var connected = await ConnectWithRetryAsync(manager, stoppingToken);
            if (!connected) {
                return;
            }

            if (_options.AutoStart) {
                var started = await _safeExecutor.ExecuteAsync(
                    token => manager.StartAsync(token),
                    "LoopTrackManagerService.StartAsync",
                    false,
                    stoppingToken);

                if (started.Success && started.Result) {
                    var setSpeedResult = await _safeExecutor.ExecuteAsync(
                        token => manager.SetTargetSpeedAsync(_options.TargetSpeedMmps, token),
                        "LoopTrackManagerService.SetTargetSpeedAsync",
                        false,
                        stoppingToken);

                    if (!setSpeedResult.Success || !setSpeedResult.Result) {
                        _logger.LogWarning("LoopTrack 自动设速失败，目标速度={TargetSpeedMmps}mm/s。", _options.TargetSpeedMmps);
                    }
                }
                else {
                    _logger.LogWarning("LoopTrack 自动启动失败。");
                }
            }

            var statusTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(pollingIntervalMs));
            try {
                while (await statusTimer.WaitForNextTickAsync(stoppingToken)) {
                    _safeExecutor.Execute(
                        () => _logger.LogDebug(
                            "LoopTrack状态 Name={TrackName} Conn={ConnectionStatus} Run={RunStatus} Stabilization={StabilizationStatus} Target={TargetSpeedMmps}mm/s RealTime={RealTimeSpeedMmps}mm/s",
                            manager.TrackName,
                            manager.ConnectionStatus,
                            manager.RunStatus,
                            manager.StabilizationStatus,
                            manager.TargetSpeedMmps,
                            manager.RealTimeSpeedMmps),
                        "LoopTrackManagerService.LogStatus");
                }
            }
            catch (OperationCanceledException) {
                _logger.LogInformation("LoopTrack 后台服务收到停止信号。");
            }
            finally {
                statusTimer.Dispose();
            }
        }

        /// <summary>
        /// 停止后台服务并释放资源。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        public override async Task StopAsync(CancellationToken cancellationToken) {
            await base.StopAsync(cancellationToken);

            var manager = _manager;
            if (manager is null) {
                return;
            }

            await _safeExecutor.ExecuteAsync(
                token => manager.StopAsync(token),
                "LoopTrackManagerService.StopAsync",
                false,
                cancellationToken);

            await _safeExecutor.ExecuteAsync(
                token => manager.DisconnectAsync(token),
                "LoopTrackManagerService.DisconnectAsync",
                cancellationToken);

            await _safeExecutor.ExecuteAsync(
                () => manager.DisposeAsync().AsTask(),
                "LoopTrackManagerService.DisposeAsync");

            _manager = null;
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

            manager.Faulted += (_, args) => _safeExecutor.Execute(
                () => _logger.LogError(args.Exception, "LoopTrack故障事件 Operation={Operation}", args.Operation),
                "LoopTrackManagerService.Faulted");
        }

        /// <summary>
        /// 执行连接并在失败时按固定间隔重试。
        /// </summary>
        /// <param name="manager">环轨管理器。</param>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>连接是否成功。</returns>
        private async Task<bool> ConnectWithRetryAsync(LeiMaLoopTrackManager manager, CancellationToken stoppingToken) {
            while (!stoppingToken.IsCancellationRequested) {
                var connected = await _safeExecutor.ExecuteAsync(
                    token => manager.ConnectAsync(token),
                    "LoopTrackManagerService.ConnectAsync",
                    false,
                    stoppingToken);

                if (connected.Success && connected.Result) {
                    return true;
                }

                _logger.LogWarning("LoopTrack 连接失败，{DelaySeconds}s 后重试。", _connectRetryDelay.TotalSeconds);
                try {
                    await Task.Delay(_connectRetryDelay, stoppingToken);
                }
                catch (OperationCanceledException) {
                    break;
                }
            }

            _logger.LogInformation("LoopTrack 停止重连，后台服务准备退出。");
            return false;
        }

        /// <summary>
        /// 校验服务配置合法性。
        /// </summary>
        /// <param name="options">服务配置。</param>
        /// <param name="validationMessage">校验消息。</param>
        /// <returns>配置是否有效。</returns>
        private static bool TryValidateOptions(LoopTrackServiceOptions options, out string validationMessage) {
            if (string.IsNullOrWhiteSpace(options.TrackName)) {
                validationMessage = "TrackName 不能为空。";
                return false;
            }

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

            if (options.LeiMaConnection.MaxTorqueRawUnit == 0) {
                validationMessage = "LeiMaConnection.MaxTorqueRawUnit 不能为 0。";
                return false;
            }

            validationMessage = string.Empty;
            return true;
        }
    }
}
