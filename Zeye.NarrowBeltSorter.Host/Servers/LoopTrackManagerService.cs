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

            var connected = await _safeExecutor.ExecuteAsync(
                token => manager.ConnectAsync(token),
                "LoopTrackManagerService.ConnectAsync",
                false,
                stoppingToken,
                ex => PublishLoopTrackFault("LoopTrackManagerService.ConnectAsync", ex));

            if (!connected.Success || !connected.Result) {
                _logger.LogError("LoopTrack 连接失败，后台服务退出。");
                return;
            }

            if (_options.AutoStart) {
                var started = await _safeExecutor.ExecuteAsync(
                    token => _manager.StartAsync(token),
                    "LoopTrackManagerService.StartAsync",
                    false,
                    stoppingToken,
                    ex => PublishLoopTrackFault("LoopTrackManagerService.StartAsync", ex));

                if (started.Success && started.Result) {
                    var setSpeedResult = await _safeExecutor.ExecuteAsync(
                        token => manager.SetTargetSpeedAsync(_options.TargetSpeedMmps, token),
                        "LoopTrackManagerService.SetTargetSpeedAsync",
                        false,
                        stoppingToken,
                        ex => PublishLoopTrackFault("LoopTrackManagerService.SetTargetSpeedAsync", ex));

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
                    if (_manager is null) {
                        break;
                    }

                    _safeExecutor.Execute(
                        () => _logger.LogInformation(
                            "LoopTrack状态 Name={TrackName} Conn={ConnectionStatus} Run={RunStatus} Stabilization={StabilizationStatus} Target={TargetSpeedMmps}mm/s RealTime={RealTimeSpeedMmps}mm/s",
                            _manager.TrackName,
                            _manager.ConnectionStatus,
                            _manager.RunStatus,
                            _manager.StabilizationStatus,
                            _manager.TargetSpeedMmps,
                            _manager.RealTimeSpeedMmps),
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
            var manager = _manager;
            if (manager is null) {
                await base.StopAsync(cancellationToken);
                return;
            }

            await _safeExecutor.ExecuteAsync(
                token => manager.StopAsync(token),
                "LoopTrackManagerService.StopAsync",
                false,
                cancellationToken,
                ex => PublishLoopTrackFault("LoopTrackManagerService.StopAsync", ex));

            await _safeExecutor.ExecuteAsync(
                token => manager.DisconnectAsync(token),
                "LoopTrackManagerService.DisconnectAsync",
                cancellationToken,
                ex => PublishLoopTrackFault("LoopTrackManagerService.DisconnectAsync", ex));

            await _safeExecutor.ExecuteAsync(
                () => manager.DisposeAsync().AsTask(),
                "LoopTrackManagerService.DisposeAsync");

            _manager = null;
            await base.StopAsync(cancellationToken);
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
        /// 发布服务内部异常日志。
        /// </summary>
        /// <param name="operation">操作名。</param>
        /// <param name="exception">异常对象。</param>
        private void PublishLoopTrackFault(string operation, Exception exception) {
            _safeExecutor.Execute(
                () => _logger.LogError(exception, "LoopTrack服务异常 Operation={Operation}", operation),
                "LoopTrackManagerService.PublishFault");
        }
    }
}
