using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Enums.SignalTower;
using Zeye.NarrowBeltSorter.Core.Events.IoPanel;
using Zeye.NarrowBeltSorter.Core.Events.System;
using Zeye.NarrowBeltSorter.Core.Manager.IoPanel;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Manager.SignalTower;

namespace Zeye.NarrowBeltSorter.Execution.Services.Hosted {

    /// <summary>
    /// 检修托管服务。
    /// <para>职责：</para>
    /// <list type="bullet">
    ///   <item>监听 IoPanel 检修开关（<see cref="Core.Enums.Io.IoPanelButtonType.MaintenanceSwitch"/>）事件。</item>
    ///   <item>开关打开时：若处于急停则蜂鸣 5 秒；否则切换至 Maintenance 状态（轨道由 LoopTrackManagerHostedService 统一驱动）。</item>
    ///   <item>开关关闭时：切换至暂停状态（急停期间忽略）。</item>
    ///   <item>检修开关打开期间阻止系统进入运行状态（拦截并强制回到 Maintenance 状态）。</item>
    /// </list>
    /// </summary>
    public sealed class MaintenanceHostedService : BackgroundService {

        private static readonly EventId MaintenanceEventId = new(9100, "Maintenance");

        /// <summary>从运行态切换至暂停后、进入检修状态前的等待时长（毫秒）。</summary>
        private const int PauseToMaintenanceTransitionDelayMs = 300;

        /// <summary>急停状态下触发检修时蜂鸣器持续蜂鸣时长（毫秒）。</summary>
        private const int EmergencyMaintenanceBuzzerDurationMs = 5000;
        /// <summary>检修事件通道容量（条）。</summary>
        private const int MaintenanceEventChannelCapacity = 512;

        private readonly ILogger<MaintenanceHostedService> _logger;
        private readonly SafeExecutor _safeExecutor;
        private readonly IIoPanel _ioPanel;
        private readonly ISystemStateManager _systemStateManager;
        private readonly ISignalTower? _signalTower;

        /// <summary>检修开关当前是否处于打开（触发）状态。</summary>
        private volatile bool _maintenanceSwitchOpen;

        /// <summary>服务停止令牌，保存自 ExecuteAsync，用于后台任务调度。</summary>
        private CancellationToken _stoppingToken;

        /// <summary>
        /// 当前已打开会话的取消源；每次 opened 事件创建新实例，closed 时取消以中止等待中的流程。
        /// </summary>
        private CancellationTokenSource? _openedSessionCts;

        /// <summary>打开会话同步锁。</summary>
        private readonly object _sessionLock = new();

        /// <summary>蜂鸣会话取消源（用于急停+检修触发场景的 5 秒蜂鸣）。</summary>
        private CancellationTokenSource? _buzzerCts;

        /// <summary>蜂鸣同步锁。</summary>
        private readonly object _buzzerLock = new();

        private EventHandler<IoPanelButtonPressedEventArgs>? _switchOpenedHandler;
        private EventHandler<IoPanelButtonReleasedEventArgs>? _switchClosedHandler;
        private EventHandler<StateChangeEventArgs>? _stateChangedHandler;

        /// <summary>检修事件有序通道（单消费者）。</summary>
        private readonly Channel<MaintenanceCommand> _maintenanceEventChannel = CreateMaintenanceEventChannel();

        /// <summary>检修事件通道关闭标志。</summary>
        private bool _maintenanceEventChannelCompleted;
        /// <summary>检修事件累计丢弃数量。</summary>
        private long _droppedMaintenanceEventCount;
        /// <summary>检修事件最近一次丢弃告警时间刻（毫秒）。</summary>
        private long _lastMaintenanceDropWarningElapsedMs;

        /// <summary>检修事件命令。</summary>
        private readonly record struct MaintenanceCommand(
            byte CommandType,
            CancellationToken SessionToken);

        /// <summary>检修事件命令类型。</summary>
        private static class MaintenanceCommandTypes {
            internal const byte SwitchOpened = 1;
            internal const byte SwitchClosed = 2;
            internal const byte BlockRunning = 3;
        }

        /// <summary>
        /// 初始化检修托管服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <param name="ioPanel">IoPanel 管理器。</param>
        /// <param name="systemStateManager">系统状态管理器。</param>
        /// <param name="signalTower">信号塔（可选，未配置时为 null）。</param>
        public MaintenanceHostedService(
            ILogger<MaintenanceHostedService> logger,
            SafeExecutor safeExecutor,
            IIoPanel ioPanel,
            ISystemStateManager systemStateManager,
            ISignalTower? signalTower = null) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _ioPanel = ioPanel ?? throw new ArgumentNullException(nameof(ioPanel));
            _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));
            _signalTower = signalTower;
        }

        /// <summary>
        /// 订阅 IoPanel 检修开关事件与系统状态变更事件，并保活直到服务停止。
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            _stoppingToken = stoppingToken;
            SubscribeEvents();
            _logger.LogInformation(MaintenanceEventId, "MaintenanceHostedService 已启动，监听检修开关（IoPanel.MaintenanceSwitchOpened/Closed）。");
            try {
                await ConsumeMaintenanceEventChannelAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                // 正常停止，忽略取消异常。
            }
            finally {
                UnsubscribeEvents();
                CancelOpenedSession();
                CancelBuzzer();
                Volatile.Write(ref _maintenanceEventChannelCompleted, true);
                _maintenanceEventChannel.Writer.TryComplete();
            }
        }

        /// <summary>
        /// 停止时取消会话与蜂鸣，并退订事件。
        /// </summary>
        public override Task StopAsync(CancellationToken cancellationToken) {
            CancelOpenedSession();
            CancelBuzzer();
            UnsubscribeEvents();
            Volatile.Write(ref _maintenanceEventChannelCompleted, true);
            _maintenanceEventChannel.Writer.TryComplete();
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

            // 步骤1：为本次打开建立会话级取消源，与服务停止令牌关联。
            CancellationTokenSource newSession;
            CancellationTokenSource? oldSession;
            lock (_sessionLock) {
                oldSession = _openedSessionCts;
                newSession = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
                _openedSessionCts = newSession;
            }
            oldSession?.Cancel();
            oldSession?.Dispose();

            TryEnqueueMaintenanceCommand(new MaintenanceCommand(
                MaintenanceCommandTypes.SwitchOpened,
                newSession.Token));
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

            // 步骤1：取消当前打开会话（中止正在等待中的 300ms 过渡）。
            CancelOpenedSession();
            TryEnqueueMaintenanceCommand(new MaintenanceCommand(
                MaintenanceCommandTypes.SwitchClosed,
                _stoppingToken));
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

            TryEnqueueMaintenanceCommand(new MaintenanceCommand(
                MaintenanceCommandTypes.BlockRunning,
                _stoppingToken));
        }

        /// <summary>
        /// 写入检修事件命令通道（满载时聚合告警）。
        /// </summary>
        /// <param name="command">检修命令。</param>
        private void TryEnqueueMaintenanceCommand(MaintenanceCommand command) {
            if (_maintenanceEventChannel.Writer.TryWrite(command)) {
                return;
            }

            if (Volatile.Read(ref _maintenanceEventChannelCompleted)) {
                _logger.LogDebug("检修事件通道已关闭，忽略命令 CommandType={CommandType}", command.CommandType);
                return;
            }

            var dropped = Interlocked.Increment(ref _droppedMaintenanceEventCount);
            var currentElapsedMs = Environment.TickCount64;
            var lastMs = Volatile.Read(ref _lastMaintenanceDropWarningElapsedMs);
            if (unchecked(currentElapsedMs - lastMs) >= 1000 &&
                Interlocked.CompareExchange(ref _lastMaintenanceDropWarningElapsedMs, currentElapsedMs, lastMs) == lastMs) {
                _logger.LogWarning(
                    MaintenanceEventId,
                    "检修事件通道持续满载，已聚合丢弃 DroppedCount={DroppedCount} CommandType={CommandType}",
                    dropped,
                    command.CommandType);
            }
        }

        /// <summary>
        /// 按 FIFO 顺序消费检修事件命令。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task ConsumeMaintenanceEventChannelAsync(CancellationToken stoppingToken) {
            await foreach (var command in _maintenanceEventChannel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false)) {
                try {
                    await _safeExecutor.ExecuteAsync(
                        async token => {
                            switch (command.CommandType) {
                                case MaintenanceCommandTypes.SwitchOpened:
                                    await HandleMaintenanceSwitchOpenedAsync(command.SessionToken).ConfigureAwait(false);
                                    break;
                                case MaintenanceCommandTypes.SwitchClosed:
                                    await HandleMaintenanceSwitchClosedAsync(command.SessionToken).ConfigureAwait(false);
                                    break;
                                case MaintenanceCommandTypes.BlockRunning:
                                    await EnsureMaintenanceStateAsync(command.SessionToken).ConfigureAwait(false);
                                    break;
                            }
                        },
                        $"MaintenanceHostedService.{command.CommandType}",
                        command.SessionToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    // 正常取消路径。
                }
                catch (Exception ex) {
                    _logger.LogError(
                        MaintenanceEventId,
                        ex,
                        "处理检修事件命令异常 CommandType={CommandType}",
                        command.CommandType);
                }
            }
        }

        /// <summary>
        /// 创建检修命令通道配置并返回通道实例。
        /// </summary>
        /// <returns>检修命令通道。</returns>
        private static Channel<MaintenanceCommand> CreateMaintenanceEventChannel() {
            var maintenanceEventChannelOptions = new BoundedChannelOptions(MaintenanceEventChannelCapacity) {
                SingleReader = true,
                SingleWriter = false
            };
            maintenanceEventChannelOptions.FullMode = BoundedChannelFullMode.DropWrite;
            return Channel.CreateBounded<MaintenanceCommand>(maintenanceEventChannelOptions);
        }

        /// <summary>
        /// 确保系统状态切换至 Maintenance（拦截运行态时调用）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task EnsureMaintenanceStateAsync(CancellationToken cancellationToken) {
            if (_systemStateManager.CurrentState == SystemState.Maintenance) {
                return;
            }

            var changed = await _systemStateManager.ChangeStateAsync(SystemState.Maintenance, cancellationToken).ConfigureAwait(false);
            if (!changed) {
                _logger.LogWarning(
                    MaintenanceEventId,
                    "拦截运行态：切换至检修状态失败，当前状态={State}。",
                    _systemStateManager.CurrentState);
            }
        }

        /// <summary>
        /// 检修开关打开时的处理流程。
        /// </summary>
        /// <param name="sessionToken">本次打开会话的取消令牌（closed 时会取消）。</param>
        private async Task HandleMaintenanceSwitchOpenedAsync(CancellationToken sessionToken) {
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

            // 步骤2：若当前处于运行状态，先切换到暂停状态，等待过渡延迟。
            if (currentState == SystemState.Running) {
                _logger.LogInformation(
                    MaintenanceEventId,
                    "检修开关打开，当前处于运行状态，切换至暂停状态后等待 {DelayMs}ms 再进入检修状态。",
                    PauseToMaintenanceTransitionDelayMs);
                var paused = await _systemStateManager.ChangeStateAsync(SystemState.Paused, sessionToken).ConfigureAwait(false);
                if (!paused) {
                    _logger.LogWarning(MaintenanceEventId, "切换至暂停状态失败，当前状态={State}。", _systemStateManager.CurrentState);
                }

                try {
                    await Task.Delay(PauseToMaintenanceTransitionDelayMs, sessionToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    // 开关在过渡延迟内已关闭，中止流程。
                    _logger.LogInformation(MaintenanceEventId, "检修开关在过渡等待期间已关闭，放弃切换至检修状态。");
                    return;
                }
            }

            // 步骤3：再次确认开关仍处于打开状态。
            if (!_maintenanceSwitchOpen) {
                _logger.LogInformation(MaintenanceEventId, "检修开关在过渡后已关闭，放弃切换至检修状态。");
                return;
            }

            // 步骤4：切换到检修状态；轨道由 LoopTrackManagerHostedService 统一驱动。
            _logger.LogInformation(MaintenanceEventId, "检修开关打开，切换至检修状态。CurrentState={State}。", _systemStateManager.CurrentState);
            var changed = await _systemStateManager.ChangeStateAsync(SystemState.Maintenance, sessionToken).ConfigureAwait(false);
            if (!changed) {
                _logger.LogWarning(MaintenanceEventId, "切换至检修状态失败，当前状态={State}。", _systemStateManager.CurrentState);
            }
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

            // 步骤2：切换至暂停状态；轨道停止由 LoopTrackManagerHostedService 统一驱动。
            _logger.LogInformation(MaintenanceEventId, "检修开关关闭，切换至暂停状态。CurrentState={State}。", currentState);
            var changed = await _systemStateManager.ChangeStateAsync(SystemState.Paused, cancellationToken).ConfigureAwait(false);
            if (!changed) {
                _logger.LogWarning(MaintenanceEventId, "切换至暂停状态失败，当前状态={State}。", _systemStateManager.CurrentState);
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

            // 步骤1：取消旧蜂鸣会话并创建新会话令牌，关联服务停止令牌。
            CancellationTokenSource newCts;
            CancellationTokenSource? old;
            lock (_buzzerLock) {
                old = _buzzerCts;
                newCts = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
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
                // 步骤3：等待指定时长，可被新会话或服务停止取消。
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
        /// 取消当前已打开会话。
        /// </summary>
        private void CancelOpenedSession() {
            CancellationTokenSource? cts;
            lock (_sessionLock) {
                cts = _openedSessionCts;
                _openedSessionCts = null;
            }
            if (cts is null) {
                return;
            }
            cts.Cancel();
            cts.Dispose();
        }

        /// <summary>
        /// 取消当前活跃的蜂鸣会话（服务停止时调用）。
        /// </summary>
        private void CancelBuzzer() {
            CancellationTokenSourceHelper.CancelAndDispose(_buzzerLock, ref _buzzerCts);
        }
    }
}
