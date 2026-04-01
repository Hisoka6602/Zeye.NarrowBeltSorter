using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Events.Track;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Utilities.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 环轨上机联调后台服务。
    /// </summary>
    public class LoopTrackHILHostedService : LoopTrackManagerHostedService {

        /// <summary>
        /// 状态分类日志事件编号（41xx 段用于 LoopTrack 分类日志）。
        /// </summary>
        private static readonly EventId LoopTrackStatusEventId = new(4101, "looptrack-status");

        /// <summary>
        /// PID 分类日志事件编号（41xx 段用于 LoopTrack 分类日志）。
        /// </summary>
        private static readonly EventId LoopTrackSpeedEventId = new(4104, "looptrack-speed");

        /// <summary>
        /// 故障分类日志事件编号（41xx 段用于 LoopTrack 分类日志）。
        /// </summary>
        private static readonly EventId LoopTrackFaultEventId = new(4103, "looptrack-fault");

        /// <summary>
        /// 初始化上机联调后台服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <param name="options">主服务配置。</param>
        /// <param name="systemStateManager"></param>
        public LoopTrackHILHostedService(
            ILogger<LoopTrackHILHostedService> logger,
            SafeExecutor safeExecutor,
            IOptionsMonitor<LoopTrackServiceOptions> options,
            ISystemStateManager systemStateManager)
            : base(logger, safeExecutor, options, systemStateManager) {
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
                Logger.LogInformation(LoopTrackStatusEventId, "LoopTrack HIL 模式已禁用。");
                return;
            }

            if (!TryValidateOptions(options, out var validationMessage)) {
                Logger.LogError(LoopTrackFaultEventId, "LoopTrack HIL 基础配置无效，后台服务退出。原因：{ValidationMessage}", validationMessage);
                return;
            }

            if (!TryValidateHilOptions(options, out validationMessage)) {
                Logger.LogError(LoopTrackFaultEventId, "LoopTrack HIL 配置无效，后台服务退出。原因：{ValidationMessage}", validationMessage);
                return;
            }

            // 步骤1：创建环轨管理器并绑定全量联调事件。
            var pollingInterval = TimeSpan.FromMilliseconds(options.PollingIntervalMs);
            var manager = CreateManager(pollingInterval);
            _manager = manager;
            BindEvents(manager);
            Logger.LogInformation(
                LoopTrackStatusEventId,
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
                await SafeStopAndDisconnectAsync(manager, "LoopTrackHILHostedService.AutoStartCompensation", stoppingToken);
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
                    Logger.LogInformation(LoopTrackStatusEventId, "LoopTrack HIL 键盘停轨监听任务已按取消请求正常结束。");
                }
                catch (Exception ex) {
                    Logger.LogError(LoopTrackFaultEventId, ex, "LoopTrack HIL 键盘停轨监听任务异常结束。");
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
                "LoopTrackHILHostedService.ConnectionStatusChanged");

            manager.RunStatusChanged += (_, args) => SafeExecutor.Execute(
                () => OnRunStatusChanged(args),
                "LoopTrackHILHostedService.RunStatusChanged");

            manager.SpeedChanged += (_, args) => SafeExecutor.Execute(
                () => OnSpeedChanged(args),
                "LoopTrackHILHostedService.SpeedChanged");

            manager.StabilizationStatusChanged += (_, args) => SafeExecutor.Execute(
                () => OnStabilizationStatusChanged(args),
                "LoopTrackHILHostedService.StabilizationStatusChanged");

            manager.StabilizationReset += (_, args) => SafeExecutor.Execute(
                () => OnStabilizationReset(args),
                "LoopTrackHILHostedService.StabilizationReset");

            manager.TargetSpeedClamped += (_, args) => SafeExecutor.Execute(
                () => OnTargetSpeedClamped(args),
                "LoopTrackHILHostedService.TargetSpeedClamped");

            manager.SpeedNotReached += (_, args) => SafeExecutor.Execute(
                () => OnSpeedNotReached(args),
                "LoopTrackHILHostedService.SpeedNotReached");

            manager.LowFrequencySetpointDetected += (_, args) => SafeExecutor.Execute(
                () => OnLowFrequencySetpointDetected(args),
                "LoopTrackHILHostedService.LowFrequencySetpointDetected");

            manager.SpeedSpreadTooLargeDetected += (_, args) => SafeExecutor.Execute(
                () => OnSpeedSpreadTooLargeDetected(args),
                "LoopTrackHILHostedService.SpeedSpreadTooLargeDetected");

            manager.SpeedSamplingPartiallyFailed += (_, args) => SafeExecutor.Execute(
                () => OnSpeedSamplingPartiallyFailed(args),
                "LoopTrackHILHostedService.SpeedSamplingPartiallyFailed");

            manager.FrequencySetpointHardClamped += (_, args) => SafeExecutor.Execute(
                () => OnFrequencySetpointHardClamped(args),
                "LoopTrackHILHostedService.FrequencySetpointHardClamped");

            manager.Faulted += (_, args) => SafeExecutor.Execute(
                () => OnFaulted(args),
                "LoopTrackHILHostedService.Faulted");
        }

        /// <summary>
        /// 输出连接状态变化日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnConnectionStatusChanged(LoopTrackConnectionStatusChangedEventArgs args) {
            Logger.LogInformation(LoopTrackStatusEventId, "HIL连接状态变化 Old={OldStatus} New={NewStatus} ChangedAt={ChangedAt} Message={Message}", args.OldStatus, args.NewStatus, args.ChangedAt, args.Message);
        }

        /// <summary>
        /// 输出运行状态变化日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnRunStatusChanged(LoopTrackRunStatusChangedEventArgs args) {
            Logger.LogInformation(LoopTrackStatusEventId, "HIL运行状态变化 Old={OldStatus} New={NewStatus} ChangedAt={ChangedAt} Message={Message}", args.OldStatus, args.NewStatus, args.ChangedAt, args.Message);
        }

        /// <summary>
        /// 输出速度变化日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnSpeedChanged(LoopTrackSpeedChangedEventArgs args) {
            if (Options.Hil.EnableVerboseEventLog) {
                Logger.LogDebug(LoopTrackStatusEventId, "HIL速度变化 Target={TargetSpeedMmps} Real={NewRealTimeSpeedMmps} ChangedAt={ChangedAt}", args.TargetSpeedMmps, args.NewRealTimeSpeedMmps, args.ChangedAt);
            }
        }

        /// <summary>
        /// 输出稳速状态变化日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnStabilizationStatusChanged(LoopTrackStabilizationStatusChangedEventArgs args) {
            Logger.LogInformation(LoopTrackStatusEventId, "HIL稳速状态变化 Old={OldStatus} New={NewStatus} Elapsed={StabilizationElapsed} ChangedAt={ChangedAt} Message={Message}", args.OldStatus, args.NewStatus, args.StabilizationElapsed, args.ChangedAt, args.Message);
        }

        /// <summary>
        /// 输出稳速重置日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnStabilizationReset(LoopTrackStabilizationResetEventArgs args) {
            Logger.LogWarning(LoopTrackFaultEventId, "HIL稳速状态已重置 Reason={Reason} OccurredAt={OccurredAt}", args.Reason, args.OccurredAt);
        }

        /// <summary>
        /// 输出目标速度限幅日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnTargetSpeedClamped(LoopTrackTargetSpeedClampedEventArgs args) {
            Logger.LogWarning(LoopTrackFaultEventId, "HIL目标速度限幅 Operation={Operation} RequestedMmps={RequestedMmps} LimitedMmps={LimitedMmps} ClampMaxHz={ClampMaxHz} MmpsPerHz={MmpsPerHz} OccurredAt={OccurredAt}", args.Operation, args.RequestedMmps, args.LimitedMmps, args.ClampMaxHz, args.MmpsPerHz, args.OccurredAt);
        }

        /// <summary>
        /// 输出速度未达标日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnSpeedNotReached(LoopTrackSpeedNotReachedEventArgs args) {
            Logger.LogWarning(LoopTrackFaultEventId, "HIL速度未达标 TargetMmps={TargetMmps} ActualMmps={ActualMmps} TargetHz={TargetHz} ActualHz={ActualHz} IssuedHz={IssuedHz} GapHz={GapHz} LimitReason={LimitReason} OccurredAt={OccurredAt}", args.TargetMmps, args.ActualMmps, args.TargetHz, args.ActualHz, args.IssuedHz, args.GapHz, args.LimitReason, args.OccurredAt);
        }

        /// <summary>
        /// 输出低频给定日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnLowFrequencySetpointDetected(LoopTrackLowFrequencySetpointEventArgs args) {
            Logger.LogWarning(LoopTrackFaultEventId, "HIL低频给定告警 EstimatedMmps={EstimatedMmps} RawUnit={RawUnit} TargetHz={TargetHz} ThresholdHz={ThresholdHz} OccurredAt={OccurredAt}", args.EstimatedMmps, args.RawUnit, args.TargetHz, args.ThresholdHz, args.OccurredAt);
        }

        /// <summary>
        /// 输出多从站速度离散度日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnSpeedSpreadTooLargeDetected(LoopTrackSpeedSpreadTooLargeEventArgs args) {
            Logger.LogWarning(LoopTrackFaultEventId, "HIL速度离散过大 Strategy={Strategy} SpreadMmps={SpreadMmps} Samples={Samples} OccurredAt={OccurredAt}", args.Strategy, args.SpreadMmps, args.Samples, args.OccurredAt);
        }

        /// <summary>
        /// 输出速度采样部分失败日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnSpeedSamplingPartiallyFailed(LoopTrackSpeedSamplingPartiallyFailedEventArgs args) {
            Logger.LogWarning(LoopTrackFaultEventId, "HIL速度采样部分失败 SuccessCount={SuccessCount} FailCount={FailCount} FailedSlaves={FailedSlaves} OccurredAt={OccurredAt}", args.SuccessCount, args.FailCount, args.FailedSlaveIds, args.OccurredAt);
        }

        /// <summary>
        /// 输出频率硬限幅日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnFrequencySetpointHardClamped(LoopTrackFrequencySetpointHardClampedEventArgs args) {
            Logger.LogWarning(LoopTrackFaultEventId, "HIL频率硬限幅 RequestedRaw={RequestedRawUnit} RequestedHz={RequestedHz} ClampMaxHz={ClampMaxHz} ClampedRaw={ClampedRawUnit} OccurredAt={OccurredAt}", args.RequestedRawUnit, args.RequestedHz, args.ClampMaxHz, args.ClampedRawUnit, args.OccurredAt);
        }

        /// <summary>
        /// 输出故障日志。
        /// </summary>
        /// <param name="args">事件参数。</param>
        protected virtual void OnFaulted(LoopTrackManagerFaultedEventArgs args) {
            Logger.LogError(LoopTrackFaultEventId, args.Exception, "HIL故障事件 OperationId={OperationId} Operation={Operation} FaultedAt={FaultedAt}", CreateOperationId(), args.Operation, args.FaultedAt);
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
            var targetSpeedMmps = Options.TargetSpeedMmps;
            if (!hil.AutoStartAfterConnect && hil.AutoSetInitialTargetAfterConnect && targetSpeedMmps > 0m) {
                Logger.LogWarning(
                    LoopTrackFaultEventId,
                    "HIL当前配置为仅设定目标速度但不自动启动轨道：AutoStartAfterConnect={AutoStartAfterConnect} AutoSetInitialTargetAfterConnect={AutoSetInitialTargetAfterConnect} TargetSpeedMmps={TargetSpeedMmps}。在未运行状态下，实时速度读数保持 0 属于预期行为。",
                    hil.AutoStartAfterConnect,
                    hil.AutoSetInitialTargetAfterConnect,
                    targetSpeedMmps);
            }
            if (hil.AutoClearAlarmAfterConnect) {
                var clearAlarm = await SafeExecutor.ExecuteAsync(
                    token => manager.ClearAlarmAsync(token),
                    "LoopTrackHILHostedService.ClearAlarmAsync",
                    false,
                    stoppingToken);
                if (!clearAlarm.Success || !clearAlarm.Result) {
                    Logger.LogWarning(LoopTrackFaultEventId, "HIL自动清报警失败，继续后续流程。");
                }
            }

            if (!hil.AutoStartAfterConnect) {
                if (hil.AutoSetInitialTargetAfterConnect) {
                    var setTargetResult = await SafeExecutor.ExecuteAsync(
                        token => manager.SetTargetSpeedAsync(targetSpeedMmps, token),
                        "LoopTrackHILHostedService.SetInitialTargetSpeedAsync",
                        false,
                        stoppingToken);
                    if (!setTargetResult.Success || !setTargetResult.Result) {
                        Logger.LogWarning(LoopTrackFaultEventId, "HIL自动设定目标速度失败 TargetSpeedMmps={TargetSpeedMmps}。", targetSpeedMmps);
                        return false;
                    }
                }

                return true;
            }

            var startResult = await SafeExecutor.ExecuteAsync(
                token => manager.StartAsync(token),
                "LoopTrackHILHostedService.StartAsync",
                false,
                stoppingToken);
            if (!startResult.Success || !startResult.Result) {
                Logger.LogWarning(LoopTrackFaultEventId, "HIL自动启动失败，触发补偿链路。");
                return false;
            }
            if (hil.AutoSetInitialTargetAfterConnect) {
                var setTargetResult = await SafeExecutor.ExecuteAsync(
                    token => manager.SetTargetSpeedAsync(targetSpeedMmps, token),
                    "LoopTrackHILHostedService.SetInitialTargetSpeedAsync",
                    false,
                    stoppingToken);
                if (!setTargetResult.Success || !setTargetResult.Result) {
                    Logger.LogWarning(LoopTrackFaultEventId, "HIL自动设定目标速度失败 TargetSpeedMmps={TargetSpeedMmps}。", targetSpeedMmps);
                    return false;
                }
            }

            Logger.LogInformation(LoopTrackStatusEventId, "HIL自动启动完成。");
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
                "LoopTrackHILHostedService.ConnectAsync",
                Options.LeiMaConnection.Transport,
                token => SafeExecutor.ExecuteAsync(
                    connectToken => manager.ConnectAsync(connectToken),
                    "LoopTrackHILHostedService.ConnectAsync",
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
                Logger.LogWarning(LoopTrackFaultEventId, "HIL键盘停轨自动降级：当前环境非交互式。");
                return null;
            }

            Logger.LogInformation(LoopTrackStatusEventId, "HIL键盘停轨已启用，按键 {StopKey} 可触发停轨。", hil.StopKey);
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
                    "LoopTrackHILHostedService.KeyboardStop",
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
                Logger.LogWarning(LoopTrackFaultEventId, "HIL收到键盘停轨指令，开始执行 StopAsync。");
                var stopped = await manager.StopAsync(cancellationToken);
                if (!stopped) {
                    Logger.LogWarning(LoopTrackFaultEventId, "HIL键盘停轨执行失败。");
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
                            var targetHz = LeiMaSpeedConverter.MmpsToHz(targetSpeedMmps);
                            var realTimeHz = LeiMaSpeedConverter.MmpsToHz(realTimeSpeedMmps);
                            var deviationHz = targetHz - realTimeHz;
                            var targetRaw = LeiMaSpeedConverter.HzToRawUnit(targetHz);
                            var realTimeRaw = LeiMaSpeedConverter.HzToRawUnit(realTimeHz);
                            Logger.LogInformation(
                                LoopTrackSpeedEventId,
                                "HIL实时速度日志 采样毫秒={采样毫秒} 轨道名称={轨道名称} 连接状态={连接状态} 运行状态={运行状态} 稳速状态={稳速状态} 目标速度={目标速度}mm/s({目标频率}Hz/{目标原始值}) 实时速度={实时速度}mm/s({实时频率}Hz/{实时原始值}) 速度偏差={速度偏差}mm/s({频率偏差}Hz)",
                                watch.ElapsedMilliseconds,
                                manager.TrackName,
                                manager.ConnectionStatus,
                                manager.RunStatus,
                                manager.StabilizationStatus,
                                targetSpeedMmps,
                                targetHz,
                                targetRaw,
                                realTimeSpeedMmps,

                                realTimeHz,
                                realTimeRaw,
                                deviationMmps,
                                deviationHz);
                            if (manager.PidLastUpdatedAt.HasValue) {
                                Logger.LogInformation(
                                    LoopTrackSpeedEventId,
                                    "HIL调速日志 采样毫秒={采样毫秒} 轨道名称={轨道名称} 比例输出={比例输出}Hz 积分输出={积分输出}Hz 微分输出={微分输出}Hz 速度误差={速度误差}mm/s 命令输出={命令输出}raw 限幅前输出={限幅前输出}raw 是否限幅={是否限幅} 更新时间={更新时间}",
                                   watch.ElapsedMilliseconds,
                                    manager.TrackName,
                                    manager.PidLastProportionalHz,
                                    manager.PidLastIntegralHz,
                                    manager.PidLastDerivativeHz,
                                    manager.PidLastErrorMmps,
                                    manager.PidLastCommandOutput,
                                    manager.PidLastUnclampedOutput,
                                    manager.PidLastOutputClamped,
                                    manager.PidLastUpdatedAt);
                            }
                        },
                        "LoopTrackHILHostedService.MonitorStatusLoop");
                }
            }
            catch (OperationCanceledException) {
                Logger.LogInformation(LoopTrackStatusEventId, "HIL后台服务收到停止信号。");
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

            if (options.TargetSpeedMmps < 0m) {
                validationMessage = "TargetSpeedMmps 不能小于 0。";
                return false;
            }

            // 步骤2：校验目标速度与重试策略边界，避免越界和重试溢出。
            var maxSpeedMmps = GetMaxSpeedMmps(options.LeiMaConnection.MaxOutputHz);
            if (options.TargetSpeedMmps > maxSpeedMmps) {
                validationMessage = "TargetSpeedMmps 超出当前设备可配置速度上限。";
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
