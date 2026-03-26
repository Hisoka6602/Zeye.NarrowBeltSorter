using Microsoft.Extensions.Options;
using System.Diagnostics;
using Zeye.NarrowBeltSorter.Core.Events.Track;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Utilities.LoopTrack;

namespace Zeye.NarrowBeltSorter.Host.Services {
    /// <summary>
    /// 环轨上机联调后台服务。
    /// </summary>
    public class LoopTrackHILWorker : LoopTrackManagerService {
        /// <summary>
        /// 初始化上机联调后台服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <param name="options">主服务配置。</param>
        public LoopTrackHILWorker(
            ILogger<LoopTrackManagerService> logger,
            SafeExecutor safeExecutor,
            IOptions<LoopTrackServiceOptions> options)
            : base(logger, safeExecutor, options) {
        }

        /// <summary>
        /// 执行上机联调后台服务主循环。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            var options = Options;
            var hil = options.Hil;

            if (!hil.Enabled) {
                Logger.LogInformation("LoopTrack HIL 模式已禁用。");
                return;
            }

            if (!TryValidateOptions(options, out var validationMessage)) {
                Logger.LogError("LoopTrack HIL 基础配置无效，后台服务退出。原因：{ValidationMessage}", validationMessage);
                return;
            }

            if (!TryValidateHilOptions(options, out validationMessage)) {
                Logger.LogError("LoopTrack HIL 配置无效，后台服务退出。原因：{ValidationMessage}", validationMessage);
                return;
            }

            // 步骤1：创建环轨管理器并绑定全量联调事件。
            var pollingInterval = TimeSpan.FromMilliseconds(options.PollingIntervalMs);
            var manager = CreateManager(pollingInterval);
            _manager = manager;
            BindEvents(manager);
            Logger.LogInformation(
                "LoopTrack 运行模式=HIL Track={TrackName} Transport={Transport} AutoConnect={AutoConnectOnStart} AutoClearAlarm={AutoClearAlarmAfterConnect} AutoSetInitialTarget={AutoSetInitialTargetAfterConnect} AutoStart={AutoStartAfterConnect}",
                options.TrackName,
                options.LeiMaConnection.Transport,
                hil.AutoConnectOnStart,
                hil.AutoClearAlarmAfterConnect,
                hil.AutoSetInitialTargetAfterConnect,
                hil.AutoStartAfterConnect);

            // 步骤2：按配置执行自动连接及启动链路，失败时执行补偿并退出。
            var connected = !hil.AutoConnectOnStart || await ConnectWithHilRetryAsync(manager, hil, stoppingToken);
            if (!connected) {
                await ReleaseManagerSafelyAsync(manager);
                _manager = null;
                return;
            }

            var bootSuccess = await TryRunBootPipelineAsync(manager, hil, stoppingToken);
            if (!bootSuccess) {
                await SafeStopAndDisconnectAsync(manager, "LoopTrackHILWorker.AutoStartCompensation", stoppingToken);
                await ReleaseManagerSafelyAsync(manager);
                _manager = null;
                return;
            }

            // 步骤3：启动键盘停轨监听（可降级），并进入周期状态日志循环。
            using var keyboardCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var keyboardTask = TryStartKeyboardStopMonitorAsync(manager, hil, keyboardCts.Token);
            await MonitorHilStatusLoopAsync(manager, hil, stoppingToken);
            keyboardCts.Cancel();
            if (keyboardTask is not null) {
                try {
                    // 步骤4：等待键盘监听任务结束，取消触发属于正常退出路径。
                    await keyboardTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    Logger.LogInformation("LoopTrack HIL 键盘停轨监听任务已按取消请求正常结束。");
                }
                catch (Exception ex) {
                    Logger.LogError(ex, "LoopTrack HIL 键盘停轨监听任务异常结束。");
                }
            }
        }

        /// <summary>
        /// 绑定管理器事件并输出联调结构化日志。
        /// </summary>
        /// <param name="manager">管理器实例。</param>
        protected override void BindEvents(ILoopTrackManager manager) {
            // 步骤1：订阅所有上机联调关键事件，统一在 SafeExecutor 下隔离执行。
            manager.ConnectionStatusChanged += (_, args) => SafeExecutor.Execute(
                () => OnConnectionStatusChanged(args),
                "LoopTrackHILWorker.ConnectionStatusChanged");

            manager.RunStatusChanged += (_, args) => SafeExecutor.Execute(
                () => OnRunStatusChanged(args),
                "LoopTrackHILWorker.RunStatusChanged");

            manager.SpeedChanged += (_, args) => SafeExecutor.Execute(
                () => OnSpeedChanged(args),
                "LoopTrackHILWorker.SpeedChanged");

            manager.StabilizationStatusChanged += (_, args) => SafeExecutor.Execute(
                () => OnStabilizationStatusChanged(args),
                "LoopTrackHILWorker.StabilizationStatusChanged");

            manager.StabilizationReset += (_, args) => SafeExecutor.Execute(
                () => OnStabilizationReset(args),
                "LoopTrackHILWorker.StabilizationReset");

            manager.TargetSpeedClamped += (_, args) => SafeExecutor.Execute(
                () => OnTargetSpeedClamped(args),
                "LoopTrackHILWorker.TargetSpeedClamped");

            manager.SpeedNotReached += (_, args) => SafeExecutor.Execute(
                () => OnSpeedNotReached(args),
                "LoopTrackHILWorker.SpeedNotReached");

            manager.LowFrequencySetpointDetected += (_, args) => SafeExecutor.Execute(
                () => OnLowFrequencySetpointDetected(args),
                "LoopTrackHILWorker.LowFrequencySetpointDetected");

            manager.SpeedSpreadTooLargeDetected += (_, args) => SafeExecutor.Execute(
                () => OnSpeedSpreadTooLargeDetected(args),
                "LoopTrackHILWorker.SpeedSpreadTooLargeDetected");

            manager.SpeedSamplingPartiallyFailed += (_, args) => SafeExecutor.Execute(
                () => OnSpeedSamplingPartiallyFailed(args),
                "LoopTrackHILWorker.SpeedSamplingPartiallyFailed");

            manager.FrequencySetpointHardClamped += (_, args) => SafeExecutor.Execute(
                () => OnFrequencySetpointHardClamped(args),
                "LoopTrackHILWorker.FrequencySetpointHardClamped");

            manager.Faulted += (_, args) => SafeExecutor.Execute(
                () => OnFaulted(args),
                "LoopTrackHILWorker.Faulted");
        }

        /// <summary>
        /// 输出连接状态变化日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnConnectionStatusChanged(LoopTrackConnectionStatusChangedEventArgs args) {
            Logger.LogInformation("HIL连接状态变化 Old={OldStatus} New={NewStatus} ChangedAt={ChangedAt} Message={Message}", args.OldStatus, args.NewStatus, args.ChangedAt, args.Message);
        }

        /// <summary>
        /// 输出运行状态变化日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnRunStatusChanged(LoopTrackRunStatusChangedEventArgs args) {
            Logger.LogInformation("HIL运行状态变化 Old={OldStatus} New={NewStatus} ChangedAt={ChangedAt} Message={Message}", args.OldStatus, args.NewStatus, args.ChangedAt, args.Message);
        }

        /// <summary>
        /// 输出速度变化日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnSpeedChanged(LoopTrackSpeedChangedEventArgs args) {
            if (Options.Hil.EnableVerboseEventLog) {
                Logger.LogDebug("HIL速度变化 Target={TargetSpeedMmps} Real={NewRealTimeSpeedMmps} ChangedAt={ChangedAt}", args.TargetSpeedMmps, args.NewRealTimeSpeedMmps, args.ChangedAt);
            }
        }

        /// <summary>
        /// 输出稳速状态变化日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnStabilizationStatusChanged(LoopTrackStabilizationStatusChangedEventArgs args) {
            Logger.LogInformation("HIL稳速状态变化 Old={OldStatus} New={NewStatus} Elapsed={StabilizationElapsed} ChangedAt={ChangedAt} Message={Message}", args.OldStatus, args.NewStatus, args.StabilizationElapsed, args.ChangedAt, args.Message);
        }

        /// <summary>
        /// 输出稳速重置日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnStabilizationReset(LoopTrackStabilizationResetEventArgs args) {
            Logger.LogWarning("HIL稳速状态已重置 Reason={Reason} OccurredAt={OccurredAt}", args.Reason, args.OccurredAt);
        }

        /// <summary>
        /// 输出目标速度限幅日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnTargetSpeedClamped(LoopTrackTargetSpeedClampedEventArgs args) {
            Logger.LogWarning("HIL目标速度限幅 Operation={Operation} RequestedMmps={RequestedMmps} LimitedMmps={LimitedMmps} ClampMaxHz={ClampMaxHz} MmpsPerHz={MmpsPerHz} OccurredAt={OccurredAt}", args.Operation, args.RequestedMmps, args.LimitedMmps, args.ClampMaxHz, args.MmpsPerHz, args.OccurredAt);
        }

        /// <summary>
        /// 输出速度未达标日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnSpeedNotReached(LoopTrackSpeedNotReachedEventArgs args) {
            Logger.LogWarning("HIL速度未达标 TargetMmps={TargetMmps} ActualMmps={ActualMmps} TargetHz={TargetHz} ActualHz={ActualHz} IssuedHz={IssuedHz} GapHz={GapHz} LimitReason={LimitReason} OccurredAt={OccurredAt}", args.TargetMmps, args.ActualMmps, args.TargetHz, args.ActualHz, args.IssuedHz, args.GapHz, args.LimitReason, args.OccurredAt);
        }

        /// <summary>
        /// 输出低频给定日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnLowFrequencySetpointDetected(LoopTrackLowFrequencySetpointEventArgs args) {
            Logger.LogWarning("HIL低频给定告警 EstimatedMmps={EstimatedMmps} RawUnit={RawUnit} TargetHz={TargetHz} ThresholdHz={ThresholdHz} OccurredAt={OccurredAt}", args.EstimatedMmps, args.RawUnit, args.TargetHz, args.ThresholdHz, args.OccurredAt);
        }

        /// <summary>
        /// 输出多从站速度离散度日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnSpeedSpreadTooLargeDetected(LoopTrackSpeedSpreadTooLargeEventArgs args) {
            Logger.LogWarning("HIL速度离散过大 Strategy={Strategy} SpreadMmps={SpreadMmps} Samples={Samples} OccurredAt={OccurredAt}", args.Strategy, args.SpreadMmps, args.Samples, args.OccurredAt);
        }

        /// <summary>
        /// 输出速度采样部分失败日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnSpeedSamplingPartiallyFailed(LoopTrackSpeedSamplingPartiallyFailedEventArgs args) {
            Logger.LogWarning("HIL速度采样部分失败 SuccessCount={SuccessCount} FailCount={FailCount} OccurredAt={OccurredAt}", args.SuccessCount, args.FailCount, args.OccurredAt);
        }

        /// <summary>
        /// 输出频率硬限幅日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnFrequencySetpointHardClamped(LoopTrackFrequencySetpointHardClampedEventArgs args) {
            Logger.LogWarning("HIL频率硬限幅 RequestedRaw={RequestedRawUnit} RequestedHz={RequestedHz} ClampMaxHz={ClampMaxHz} ClampedRaw={ClampedRawUnit} OccurredAt={OccurredAt}", args.RequestedRawUnit, args.RequestedHz, args.ClampMaxHz, args.ClampedRawUnit, args.OccurredAt);
        }

        /// <summary>
        /// 输出故障日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnFaulted(LoopTrackManagerFaultedEventArgs args) {
            Logger.LogError(args.Exception, "HIL故障事件 Operation={Operation} FaultedAt={FaultedAt}", args.Operation, args.FaultedAt);
        }

        /// <summary>
        /// 执行 HIL 启动流程。
        /// </summary>
        /// <param name="manager">环轨管理器。</param>
        /// <param name="hil">HIL 配置。</param>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>流程是否成功。</returns>
        private async Task<bool> TryRunBootPipelineAsync(
            ILoopTrackManager manager,
            LoopTrackHilOptions hil,
            CancellationToken stoppingToken) {
            if (hil.AutoClearAlarmAfterConnect) {
                var clearAlarm = await SafeExecutor.ExecuteAsync(
                    token => manager.ClearAlarmAsync(token),
                    "LoopTrackHILWorker.ClearAlarmAsync",
                    false,
                    stoppingToken);
                if (!clearAlarm.Success || !clearAlarm.Result) {
                    Logger.LogWarning("HIL自动清报警失败，继续后续流程。");
                }
            }

            if (hil.AutoSetInitialTargetAfterConnect) {
                var setTargetResult = await SafeExecutor.ExecuteAsync(
                    token => manager.SetTargetSpeedAsync(hil.InitialTargetSpeedMmps, token),
                    "LoopTrackHILWorker.SetInitialTargetSpeedAsync",
                    false,
                    stoppingToken);
                if (!setTargetResult.Success || !setTargetResult.Result) {
                    Logger.LogWarning("HIL自动设定初始目标速度失败 TargetSpeedMmps={TargetSpeedMmps}。", hil.InitialTargetSpeedMmps);
                    return false;
                }
            }

            if (!hil.AutoStartAfterConnect) {
                return true;
            }

            var startResult = await SafeExecutor.ExecuteAsync(
                token => manager.StartAsync(token),
                "LoopTrackHILWorker.StartAsync",
                false,
                stoppingToken);
            if (!startResult.Success || !startResult.Result) {
                Logger.LogWarning("HIL自动启动失败，触发补偿链路。");
                return false;
            }

            Logger.LogInformation("HIL自动启动完成。");
            return true;
        }

        /// <summary>
        /// 按 HIL 配置执行连接重试。
        /// </summary>
        /// <param name="manager">环轨管理器。</param>
        /// <param name="hil">HIL 配置。</param>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>连接是否成功。</returns>
        private async Task<bool> ConnectWithHilRetryAsync(
            ILoopTrackManager manager,
            LoopTrackHilOptions hil,
            CancellationToken stoppingToken) {
            var totalAttempts = checked((long)hil.ConnectMaxAttempts + 1L);
            return await ExecuteConnectWithRetryPolicyAsync(
                totalAttempts,
                hil.ConnectRetryDelayMs,
                hil.ConnectRetryDelayMs,
                false,
                "HIL",
                "LoopTrackHILWorker.ConnectAsync",
                Options.LeiMaConnection.Transport,
                token => SafeExecutor.ExecuteAsync(
                    connectToken => manager.ConnectAsync(connectToken),
                    "LoopTrackHILWorker.ConnectAsync",
                    false,
                    token),
                stoppingToken);
        }

        /// <summary>
        /// 启动键盘停轨监听任务。
        /// </summary>
        /// <param name="manager">环轨管理器。</param>
        /// <param name="hil">HIL 配置。</param>
        /// <param name="cancellationToken">停止令牌。</param>
        /// <returns>监听任务。</returns>
        private Task? TryStartKeyboardStopMonitorAsync(
            ILoopTrackManager manager,
            LoopTrackHilOptions hil,
            CancellationToken cancellationToken) {
            if (!hil.EnableKeyboardStop) {
                return null;
            }

            var interactive = LoopTrackConsoleHelper.IsInteractive(Logger);
            if (!interactive) {
                Logger.LogWarning("HIL键盘停轨自动降级：当前环境非交互式。");
                return null;
            }

            Logger.LogInformation("HIL键盘停轨已启用，按键 {StopKey} 可触发停轨。", hil.StopKey);
            return MonitorKeyboardStopAsync(manager, cancellationToken);
        }

        /// <summary>
        /// 监控键盘输入并执行停轨。
        /// </summary>
        /// <param name="manager">环轨管理器。</param>
        /// <param name="cancellationToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task MonitorKeyboardStopAsync(ILoopTrackManager manager, CancellationToken cancellationToken) {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Options.Hil.KeyboardStopPollingIntervalMs));
            // 步骤1：轮询键盘输入，仅当检测到配置的停轨按键时触发停轨。
            while (!cancellationToken.IsCancellationRequested && await timer.WaitForNextTickAsync(cancellationToken)) {
                await SafeExecutor.ExecuteAsync(
                    token => new ValueTask(HandleKeyboardStopAsync(manager, token)),
                    "LoopTrackHILWorker.KeyboardStop",
                    cancellationToken);
            }
        }

        /// <summary>
        /// 处理键盘停轨输入。
        /// </summary>
        /// <param name="manager">环轨管理器。</param>
        /// <param name="cancellationToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task HandleKeyboardStopAsync(ILoopTrackManager manager, CancellationToken cancellationToken) {
            if (!Console.KeyAvailable) {
                return;
            }

            var key = Console.ReadKey(intercept: true);
            if (key.Key == ParseStopKey(Options.Hil.StopKey)) {
                Logger.LogWarning("HIL收到键盘停轨指令，开始执行 StopAsync。");
                var stopped = await manager.StopAsync(cancellationToken);
                if (!stopped) {
                    Logger.LogWarning("HIL键盘停轨执行失败。");
                }
            }
        }

        /// <summary>
        /// 执行 HIL 状态周期日志。
        /// </summary>
        /// <param name="manager">环轨管理器。</param>
        /// <param name="hil">HIL 配置。</param>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task MonitorHilStatusLoopAsync(
            ILoopTrackManager manager,
            LoopTrackHilOptions hil,
            CancellationToken stoppingToken) {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(hil.StatusLogIntervalMs));
            var watch = Stopwatch.StartNew();
            try {
                // 步骤1：基于配置频率输出周期状态，统一用于现场 HIL 观测。
                while (await timer.WaitForNextTickAsync(stoppingToken)) {
                    SafeExecutor.Execute(
                        () => {
                            var targetSpeedMmps = manager.TargetSpeedMmps;
                            var realTimeSpeedMmps = manager.RealTimeSpeedMmps;
                            var deviationMmps = targetSpeedMmps - realTimeSpeedMmps;
                            Logger.LogInformation(
                                "HIL状态 TickMs={TickMs} Track={Track} Connection={Connection} Run={Run} Stabilization={Stabilization} TargetMmps={TargetMmps} RealTimeMmps={RealTimeMmps} DeviationMmps={DeviationMmps}",
                                watch.ElapsedMilliseconds,
                                manager.TrackName,
                                manager.ConnectionStatus,
                                manager.RunStatus,
                                manager.StabilizationStatus,
                                targetSpeedMmps,
                                realTimeSpeedMmps,
                                deviationMmps);
                        },
                        "LoopTrackHILWorker.MonitorStatusLoop");
                }
            }
            catch (OperationCanceledException) {
                Logger.LogInformation("HIL后台服务收到停止信号。");
            }
        }

        /// <summary>
        /// 校验 HIL 配置合法性。
        /// </summary>
        /// <param name="options">主配置。</param>
        /// <param name="validationMessage">校验错误说明。</param>
        /// <returns>是否合法。</returns>
        private static bool TryValidateHilOptions(LoopTrackServiceOptions options, out string validationMessage) {
            // 步骤1：校验联调循环与键盘轮询基础时间参数。
            var hil = options.Hil;
            if (hil.StatusLogIntervalMs <= 0) {
                validationMessage = "Hil.StatusLogIntervalMs 必须大于 0。";
                return false;
            }

            if (hil.KeyboardStopPollingIntervalMs <= 0) {
                validationMessage = "Hil.KeyboardStopPollingIntervalMs 必须大于 0。";
                return false;
            }

            if (hil.InitialTargetSpeedMmps < 0m) {
                validationMessage = "Hil.InitialTargetSpeedMmps 不能小于 0。";
                return false;
            }

            // 步骤2：校验目标速度与重试策略边界，避免越界和重试溢出。
            var maxSpeedMmps = GetMaxSpeedMmps(options.LeiMaConnection.MaxOutputHz);
            if (hil.InitialTargetSpeedMmps > maxSpeedMmps) {
                validationMessage = "Hil.InitialTargetSpeedMmps 超出当前设备可配置速度上限。";
                return false;
            }

            if (hil.ConnectMaxAttempts < 0) {
                validationMessage = "Hil.ConnectMaxAttempts 不能小于 0。";
                return false;
            }

            if (hil.ConnectMaxAttempts > 20) {
                validationMessage = "Hil.ConnectMaxAttempts 不能大于 20。";
                return false;
            }

            if (hil.ConnectRetryDelayMs <= 0) {
                validationMessage = "Hil.ConnectRetryDelayMs 必须大于 0。";
                return false;
            }

            // 步骤3：校验键盘停轨按键配置可被系统键枚举识别。
            if (!Enum.TryParse<ConsoleKey>(hil.StopKey, true, out _)) {
                validationMessage = "Hil.StopKey 必须为有效 ConsoleKey 名称。";
                return false;
            }

            validationMessage = string.Empty;
            return true;
        }

        /// <summary>
        /// 计算设备速度上限（mm/s）。
        /// </summary>
        /// <param name="maxOutputHz">最大输出频率（Hz）。</param>
        /// <returns>速度上限（mm/s）。</returns>
        private static decimal GetMaxSpeedMmps(decimal maxOutputHz) {
            return maxOutputHz * LeiMaSpeedConverter.MmpsPerHz;
        }

        /// <summary>
        /// 解析键盘停轨按键。
        /// </summary>
        /// <param name="configuredStopKey">配置键名。</param>
        /// <returns>控制台按键枚举值。</returns>
        private static ConsoleKey ParseStopKey(string configuredStopKey) {
            // 步骤1：正常流程已由配置校验保证键值合法，此分支仅作为运行期防御性兜底。
            return Enum.TryParse<ConsoleKey>(configuredStopKey, true, out var parsedKey)
                ? parsedKey
                : ConsoleKey.S;
        }
    }
}
