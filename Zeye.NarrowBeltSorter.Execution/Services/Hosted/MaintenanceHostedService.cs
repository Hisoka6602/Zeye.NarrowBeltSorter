using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Enums.SignalTower;
using Zeye.NarrowBeltSorter.Core.Events.IoPanel;
using Zeye.NarrowBeltSorter.Core.Events.System;
using Zeye.NarrowBeltSorter.Core.Manager.IoPanel;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Manager.SignalTower;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;

namespace Zeye.NarrowBeltSorter.Execution.Services.Hosted {

    /// <summary>
    /// 检修托管服务。
    /// <para>职责：</para>
    /// <list type="bullet">
    ///   <item>监听 IoPanel 检修开关（<see cref="Core.Enums.Io.IoPanelButtonType.MaintenanceSwitch"/>）事件。</item>
    ///   <item>开关打开时：若处于急停则蜂鸣 5 秒；否则切换至检修状态并以检修速度驱动轨道。</item>
    ///   <item>开关关闭时：停止轨道并切换至暂停状态（急停期间忽略）。</item>
    ///   <item>检修开关打开期间阻止系统进入运行状态（拦截并强制回到检修状态）。</item>
    /// </list>
    /// </summary>
    public sealed class MaintenanceHostedService : BackgroundService {

        private static readonly EventId MaintenanceEventId = new(9100, "Maintenance");

        /// <summary>从运行态切换至暂停后、进入检修状态前的等待时长（毫秒）。</summary>
        private const int PauseToMaintenanceTransitionDelayMs = 300;

        /// <summary>急停状态下触发检修时蜂鸣器持续蜂鸣时长（毫秒）。</summary>
        private const int EmergencyMaintenanceBuzzerDurationMs = 5000;

        private readonly ILogger<MaintenanceHostedService> _logger;
        private readonly SafeExecutor _safeExecutor;
        private readonly IIoPanel _ioPanel;
        private readonly ISystemStateManager _systemStateManager;
        private readonly ILoopTrackManagerAccessor _loopTrackAccessor;
        private readonly IOptionsMonitor<LoopTrackServiceOptions> _optionsMonitor;
        private readonly ISignalTower? _signalTower;

        /// <summary>检修开关当前是否处于打开（触发）状态。</summary>
        private volatile bool _maintenanceSwitchOpen;

        /// <summary>蜂鸣会话取消源（用于急停+检修触发场景的 5 秒蜂鸣）。</summary>
        private CancellationTokenSource? _buzzerCts;

        /// <summary>蜂鸣同步锁。</summary>
        private readonly object _buzzerLock = new();

        private EventHandler<IoPanelButtonPressedEventArgs>? _switchOpenedHandler;
        private EventHandler<IoPanelButtonReleasedEventArgs>? _switchClosedHandler;
        private EventHandler<StateChangeEventArgs>? _stateChangedHandler;

        /// <summary>
        /// 初始化检修托管服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <param name="ioPanel">IoPanel 管理器。</param>
        /// <param name="systemStateManager">系统状态管理器。</param>
        /// <param name="loopTrackAccessor">环轨管理器访问器。</param>
        /// <param name="optionsMonitor">环轨服务配置监听器。</param>
        /// <param name="signalTower">信号塔（可选，未配置时为 null）。</param>
        public MaintenanceHostedService(
            ILogger<MaintenanceHostedService> logger,
            SafeExecutor safeExecutor,
            IIoPanel ioPanel,
            ISystemStateManager systemStateManager,
            ILoopTrackManagerAccessor loopTrackAccessor,
            IOptionsMonitor<LoopTrackServiceOptions> optionsMonitor,
            ISignalTower? signalTower = null) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _ioPanel = ioPanel ?? throw new ArgumentNullException(nameof(ioPanel));
            _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));
            _loopTrackAccessor = loopTrackAccessor ?? throw new ArgumentNullException(nameof(loopTrackAccessor));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _signalTower = signalTower;
        }

        /// <summary>
        /// 订阅 IoPanel 检修开关事件与系统状态变更事件，并保活直到服务停止。
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            SubscribeEvents();
            _logger.LogInformation(MaintenanceEventId, "MaintenanceHostedService 已启动，监听检修开关（IoPanel.MaintenanceSwitchOpened/Closed）。");
            try {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                // 正常停止，忽略取消异常。
            }
            finally {
                UnsubscribeEvents();
                CancelBuzzer();
            }
        }

        /// <summary>
        /// 停止时取消蜂鸣并退订事件。
        /// </summary>
        public override Task StopAsync(CancellationToken cancellationToken) {
            CancelBuzzer();
            UnsubscribeEvents();
            return base.StopAsync(cancellationToken);
        }

        /// <summary>
        /// 订阅 IoPanel 检修开关事件与系统状态变更事件。
        /// </summary>
        private void SubscribeEvents() {
            _switchOpenedHandler = (_, args) => OnMaintenanceSwitchOpened(args);
            _switchClosedHandler = (_, args) => OnMaintenanceSwitchClosed(args);
            _stateChangedHandler = (_, args) => OnSystemStateChanged(args);
            _ioPanel.MaintenanceSwitchOpened += _switchOpenedHandler;
            _ioPanel.MaintenanceSwitchClosed += _switchClosedHandler;
            _systemStateManager.StateChanged += _stateChangedHandler;
        }

        /// <summary>
        /// 退订所有已订阅事件。
        /// </summary>
        private void UnsubscribeEvents() {
            if (_switchOpenedHandler is not null) {
                _ioPanel.MaintenanceSwitchOpened -= _switchOpenedHandler;
                _switchOpenedHandler = null;
            }
            if (_switchClosedHandler is not null) {
                _ioPanel.MaintenanceSwitchClosed -= _switchClosedHandler;
                _switchClosedHandler = null;
            }
            if (_stateChangedHandler is not null) {
                _systemStateManager.StateChanged -= _stateChangedHandler;
                _stateChangedHandler = null;
            }
        }

        /// <summary>
        /// 检修开关打开处理（IoPanel.MaintenanceSwitchOpened 事件）。
        /// </summary>
        /// <param name="args">按下事件载荷。</param>
        private void OnMaintenanceSwitchOpened(IoPanelButtonPressedEventArgs args) {
            _logger.LogInformation(
                MaintenanceEventId,
                "检修开关已打开 Point={Point} ButtonName={ButtonName}。",
                args.PointId, args.ButtonName);

            _maintenanceSwitchOpen = true;
            _ = _safeExecutor.ExecuteAsync(
                token => new ValueTask(HandleMaintenanceSwitchOpenedAsync(token)),
                "MaintenanceHostedService.SwitchOpened");
        }

        /// <summary>
        /// 检修开关关闭处理（IoPanel.MaintenanceSwitchClosed 事件）。
        /// </summary>
        /// <param name="args">释放事件载荷。</param>
        private void OnMaintenanceSwitchClosed(IoPanelButtonReleasedEventArgs args) {
            _logger.LogInformation(
                MaintenanceEventId,
                "检修开关已关闭 Point={Point} ButtonName={ButtonName}。",
                args.PointId, args.ButtonName);

            _maintenanceSwitchOpen = false;
            _ = _safeExecutor.ExecuteAsync(
                token => new ValueTask(HandleMaintenanceSwitchClosedAsync(token)),
                "MaintenanceHostedService.SwitchClosed");
        }

        /// <summary>
        /// 系统状态变更处理：若检修开关打开且系统试图进入运行状态，则强制回到检修状态。
        /// </summary>
        /// <param name="args">系统状态变更事件参数。</param>
        private void OnSystemStateChanged(StateChangeEventArgs args) {
            if (!_maintenanceSwitchOpen || args.NewState != SystemState.Running) {
                return;
            }

            _logger.LogWarning(
                MaintenanceEventId,
                "检修开关打开期间拦截到运行状态切换请求，强制回到检修状态。OldState={OldState}。",
                args.OldState);

            _ = _safeExecutor.ExecuteAsync(
                token => new ValueTask(EnsureTrackRunningAtMaintenanceSafeAsync(token)),
                "MaintenanceHostedService.BlockRunning");
        }

        /// <summary>
        /// 覆盖状态至检修并确保轨道以检修速度运行（在拦截运行态时调用）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task EnsureTrackRunningAtMaintenanceSafeAsync(CancellationToken cancellationToken) {
            // 步骤1：覆盖到检修状态。
            var changed = await _systemStateManager.ChangeStateAsync(SystemState.Maintenance, cancellationToken).ConfigureAwait(false);
            if (!changed) {
                _logger.LogWarning(
                    MaintenanceEventId,
                    "拦截运行态：切换至检修状态失败，当前状态={State}，跳过轨道速度设置。",
                    _systemStateManager.CurrentState);
                return;
            }
            // 步骤2：确保轨道以检修速度运行。
            await EnsureTrackRunningAtMaintenanceSpeedAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 检修开关打开时的处理流程。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task HandleMaintenanceSwitchOpenedAsync(CancellationToken cancellationToken) {
            var currentState = _systemStateManager.CurrentState;

            // 步骤1：急停状态下不允许切换，蜂鸣 5 秒。
            if (currentState == SystemState.EmergencyStop) {
                _logger.LogWarning(
                    MaintenanceEventId,
                    "检修开关打开，但当前处于急停状态（{State}），忽略检修状态切换，触发蜂鸣 5 秒。",
                    currentState);
                await BuzzForEmergencyAsync().ConfigureAwait(false);
                return;
            }

            // 步骤2：若当前处于运行状态，先切换到暂停状态，等待配置延迟后再进入检修状态。
            if (currentState == SystemState.Running) {
                _logger.LogInformation(
                    MaintenanceEventId,
                    "检修开关打开，当前处于运行状态，切换至暂停状态后等待 {DelayMs}ms 再进入检修状态。",
                    PauseToMaintenanceTransitionDelayMs);
                var paused = await _systemStateManager.ChangeStateAsync(SystemState.Paused, cancellationToken).ConfigureAwait(false);
                if (!paused) {
                    _logger.LogWarning(MaintenanceEventId, "切换至暂停状态失败，当前状态={State}。", _systemStateManager.CurrentState);
                }
                await Task.Delay(PauseToMaintenanceTransitionDelayMs, cancellationToken).ConfigureAwait(false);
            }

            // 步骤3：切换到检修状态。
            _logger.LogInformation(MaintenanceEventId, "检修开关打开，切换至检修状态。CurrentState={State}。", _systemStateManager.CurrentState);
            var changed = await _systemStateManager.ChangeStateAsync(SystemState.Maintenance, cancellationToken).ConfigureAwait(false);
            if (!changed) {
                _logger.LogWarning(MaintenanceEventId, "切换至检修状态失败，当前状态={State}。", _systemStateManager.CurrentState);
            }

            // 步骤4：以检修速度驱动轨道。
            await EnsureTrackRunningAtMaintenanceSpeedAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 检修开关关闭时的处理流程。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task HandleMaintenanceSwitchClosedAsync(CancellationToken cancellationToken) {
            var currentState = _systemStateManager.CurrentState;

            // 步骤1：急停状态下不允许切换。
            if (currentState == SystemState.EmergencyStop) {
                _logger.LogWarning(
                    MaintenanceEventId,
                    "检修开关关闭，但当前处于急停状态（{State}），忽略状态切换。",
                    currentState);
                return;
            }

            // 步骤2：停止轨道。
            _logger.LogInformation(MaintenanceEventId, "检修开关关闭，停止轨道。");
            await StopTrackSafeAsync(cancellationToken).ConfigureAwait(false);

            // 步骤3：切换至暂停状态。
            _logger.LogInformation(MaintenanceEventId, "检修开关关闭，切换至暂停状态。CurrentState={State}。", _systemStateManager.CurrentState);
            var changed = await _systemStateManager.ChangeStateAsync(SystemState.Paused, cancellationToken).ConfigureAwait(false);
            if (!changed) {
                _logger.LogWarning(MaintenanceEventId, "切换至暂停状态失败，当前状态={State}。", _systemStateManager.CurrentState);
            }
        }

        /// <summary>
        /// 确保轨道已启动并以检修速度运行（稳速）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task EnsureTrackRunningAtMaintenanceSpeedAsync(CancellationToken cancellationToken) {
            var manager = _loopTrackAccessor.Manager;
            if (manager is null) {
                _logger.LogWarning(MaintenanceEventId, "环轨管理器未就绪，跳过检修速度设置。");
                return;
            }

            var maintenanceSpeed = _optionsMonitor.CurrentValue.MaintenanceSpeedMmps;
            _logger.LogInformation(
                MaintenanceEventId,
                "设置检修速度 MaintenanceSpeedMmps={Speed} mm/s，Track={TrackName}。",
                maintenanceSpeed, manager.TrackName);

            // 步骤1：设置目标速度。
            var setResult = await manager.SetTargetSpeedAsync(maintenanceSpeed, cancellationToken).ConfigureAwait(false);
            if (!setResult) {
                _logger.LogWarning(
                    MaintenanceEventId,
                    "检修速度设置失败，Track={TrackName} RunStatus={RunStatus}。",
                    manager.TrackName, manager.RunStatus);
            }

            // 步骤2：若轨道未在运行，则启动轨道。
            if (manager.RunStatus != Core.Enums.Track.LoopTrackRunStatus.Running) {
                _logger.LogInformation(
                    MaintenanceEventId,
                    "轨道未运行，尝试启动 Track={TrackName}。",
                    manager.TrackName);
                var startResult = await manager.StartAsync(cancellationToken).ConfigureAwait(false);
                if (!startResult) {
                    _logger.LogWarning(
                        MaintenanceEventId,
                        "检修轨道启动失败，Track={TrackName} RunStatus={RunStatus}。",
                        manager.TrackName, manager.RunStatus);
                }
            }
        }

        /// <summary>
        /// 安全停止轨道。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task StopTrackSafeAsync(CancellationToken cancellationToken) {
            var manager = _loopTrackAccessor.Manager;
            if (manager is null) {
                _logger.LogDebug(MaintenanceEventId, "环轨管理器未就绪，跳过停轨操作。");
                return;
            }
            var stopResult = await manager.StopAsync(cancellationToken).ConfigureAwait(false);
            if (!stopResult) {
                _logger.LogWarning(
                    MaintenanceEventId,
                    "检修关闭停轨失败，Track={TrackName} RunStatus={RunStatus}。",
                    manager.TrackName, manager.RunStatus);
            }
        }

        /// <summary>
        /// 急停状态下触发检修时，持续蜂鸣 5 秒。
        /// </summary>
        private async Task BuzzForEmergencyAsync() {
            if (_signalTower is null) {
                _logger.LogDebug(MaintenanceEventId, "信号塔未配置，跳过急停检修蜂鸣。");
                return;
            }

            // 步骤1：取消旧蜂鸣会话并创建新会话令牌。
            CancellationTokenSource newCts;
            CancellationTokenSource? old;
            lock (_buzzerLock) {
                old = _buzzerCts;
                newCts = new CancellationTokenSource();
                _buzzerCts = newCts;
            }
            if (old is not null) {
                old.Cancel();
                old.Dispose();
            }

            var token = newCts.Token;
            _logger.LogInformation(
                MaintenanceEventId,
                "急停状态下触发检修开关，蜂鸣器持续蜂鸣 {DurationMs}ms。",
                EmergencyMaintenanceBuzzerDurationMs);
            try {
                // 步骤2：开蜂鸣。
                await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.On, token).ConfigureAwait(false);
                // 步骤3：等待指定时长，可被新会话取消。
                await Task.Delay(EmergencyMaintenanceBuzzerDurationMs, token).ConfigureAwait(false);
                // 步骤4：关蜂鸣。
                await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.Off, token).ConfigureAwait(false);
                _logger.LogInformation(
                    MaintenanceEventId,
                    "急停检修蜂鸣 {DurationMs}ms 结束，已关闭蜂鸣器。",
                    EmergencyMaintenanceBuzzerDurationMs);
            }
            catch (OperationCanceledException) {
                // 被新会话或服务停止取消，不记录为错误。
                _logger.LogDebug(MaintenanceEventId, "急停检修蜂鸣已被取消。");
            }
            catch (Exception ex) {
                _logger.LogError(MaintenanceEventId, ex, "急停检修蜂鸣操作异常。");
            }
            finally {
                lock (_buzzerLock) {
                    if (ReferenceEquals(_buzzerCts, newCts)) {
                        _buzzerCts = null;
                    }
                }
                newCts.Dispose();
            }
        }

        /// <summary>
        /// 取消当前活跃的蜂鸣会话（服务停止时调用）。
        /// </summary>
        private void CancelBuzzer() {
            CancellationTokenSource? cts;
            lock (_buzzerLock) {
                cts = _buzzerCts;
                _buzzerCts = null;
            }
            if (cts is null) {
                return;
            }
            cts.Cancel();
            cts.Dispose();
        }
    }
}
