using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.Track;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.IoPanel;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;
using Zeye.NarrowBeltSorter.Core.Manager.IoPanel;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;

namespace Zeye.NarrowBeltSorter.Execution.Services.Hosted {

    /// <summary>
    /// IoPanel 按钮到系统状态的桥接托管服务。
    /// </summary>
    public sealed class IoPanelStateTransitionHostedService : BackgroundService {
        private static readonly TimeSpan DefaultStartupWarningDuration = TimeSpan.FromSeconds(3);
        private const int IoPanelCommandChannelCapacity = 512;
        private readonly ILogger<IoPanelStateTransitionHostedService> _logger;
        private readonly SafeExecutor _safeExecutor;
        private readonly IIoPanel _ioPanel;
        private readonly ISystemStateManager _systemStateManager;
        private readonly TimeSpan _startupWarningDuration;
        private readonly object _startupTransitionSyncRoot = new();
        private EventHandler<IoPanelButtonPressedEventArgs>? _startHandler;
        private EventHandler<IoPanelButtonPressedEventArgs>? _stopHandler;
        private EventHandler<IoPanelButtonPressedEventArgs>? _emergencyPressedHandler;
        private EventHandler<IoPanelButtonPressedEventArgs>? _resetHandler;
        private EventHandler<IoPanelButtonReleasedEventArgs>? _emergencyReleasedHandler;
        private CancellationTokenSource? _startupTransitionCts;
        private readonly ICarrierManager _carrierManager;
        private readonly ILoopTrackManagerAccessor _loopTrackAccessor;
        private readonly Channel<IoPanelTransitionCommand> _ioPanelCommandChannel =
            Channel.CreateBounded<IoPanelTransitionCommand>(
                new BoundedChannelOptions(IoPanelCommandChannelCapacity) {
                    FullMode = BoundedChannelFullMode.DropWrite,
                    SingleReader = true,
                    SingleWriter = false
                });
        private bool _ioPanelCommandChannelCompleted;
        private long _droppedIoPanelCommandCount;
        private long _lastIoPanelCommandDropWarningElapsedMs;

        private readonly record struct IoPanelTransitionCommand(
            IoPanelTransitionCommandType CommandType,
            CancellationToken StoppingToken);

        private enum IoPanelTransitionCommandType {
            StartPressed,
            StopPressed,
            EmergencyPressed,
            ResetPressed,
            EmergencyReleased,
        }

        /// <summary>
        /// 初始化 IoPanel 按钮到系统状态的桥接托管服务。
        /// </summary>
        /// <param name="logger">日志记录器。</param>
        /// <param name="safeExecutor">安全执行器。</param>
        /// <param name="ioPanel">IoPanel 管理器。</param>
        /// <param name="systemStateManager">系统状态管理器。</param>
        /// <param name="optionsMonitor">IoPanel 状态切换配置监视器。</param>
        public IoPanelStateTransitionHostedService(
            ILogger<IoPanelStateTransitionHostedService> logger,
            SafeExecutor safeExecutor,
            IIoPanel ioPanel,
            ISystemStateManager systemStateManager,
            IOptionsMonitor<LeadshaineIoPanelStateTransitionOptions> optionsMonitor,
              ICarrierManager carrierManager,
    ILoopTrackManagerAccessor loopTrackAccessor) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _ioPanel = ioPanel ?? throw new ArgumentNullException(nameof(ioPanel));
            _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));
            _carrierManager = carrierManager ?? throw new ArgumentNullException(nameof(carrierManager));
            _loopTrackAccessor = loopTrackAccessor ?? throw new ArgumentNullException(nameof(loopTrackAccessor));
            if (optionsMonitor is null) {
                throw new ArgumentNullException(nameof(optionsMonitor));
            }

            var startupWarningDurationMs = optionsMonitor.CurrentValue.StartupWarningDurationMs;
            if (startupWarningDurationMs <= 0) {
                _logger.LogWarning(
                    "IoPanelStateTransition.StartupWarningDurationMs 配置无效（<=0），回退默认值 {DefaultStartupWarningDurationMs}ms。",
                    (int)DefaultStartupWarningDuration.TotalMilliseconds);
                _startupWarningDuration = DefaultStartupWarningDuration;
            }
            else {
                _startupWarningDuration = TimeSpan.FromMilliseconds(startupWarningDurationMs);
            }
        }

        /// <summary>
        /// 托管服务主循环：挂载按钮事件后保活等待，宿主停止时解除订阅。
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            SubscribeButtons(stoppingToken);
            try {
                await ConsumeIoPanelCommandChannelAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // 宿主正常停止，退出保活等待。
            }
            finally {
                UnsubscribeButtons();
                Volatile.Write(ref _ioPanelCommandChannelCompleted, true);
                _ioPanelCommandChannel.Writer.TryComplete();
            }
        }

        /// <summary>
        /// 停止托管服务：取消启动预警迁移流程并解除全部按钮事件订阅。
        /// </summary>
        public override Task StopAsync(CancellationToken cancellationToken) {
            CancelStartupTransition("ServiceStop");
            UnsubscribeButtons();
            Volatile.Write(ref _ioPanelCommandChannelCompleted, true);
            _ioPanelCommandChannel.Writer.TryComplete();
            return base.StopAsync(cancellationToken);
        }

        /// <summary>
        /// 订阅 IoPanel 各按钮事件，并将事件映射到系统状态切换。
        /// </summary>
        /// <param name="stoppingToken">服务停止令牌。</param>
        private void SubscribeButtons(CancellationToken stoppingToken) {
            _startHandler = (_, __) => TryEnqueueIoPanelCommand(new IoPanelTransitionCommand(IoPanelTransitionCommandType.StartPressed, stoppingToken));
            _stopHandler = (_, __) => TryEnqueueIoPanelCommand(new IoPanelTransitionCommand(IoPanelTransitionCommandType.StopPressed, stoppingToken));
            _emergencyPressedHandler = (_, __) => TryEnqueueIoPanelCommand(new IoPanelTransitionCommand(IoPanelTransitionCommandType.EmergencyPressed, stoppingToken));
            _resetHandler = (_, __) => TryEnqueueIoPanelCommand(new IoPanelTransitionCommand(IoPanelTransitionCommandType.ResetPressed, stoppingToken));
            _emergencyReleasedHandler = (_, __) => TryEnqueueIoPanelCommand(new IoPanelTransitionCommand(IoPanelTransitionCommandType.EmergencyReleased, stoppingToken));

            _ioPanel.StartButtonPressed += _startHandler;
            _ioPanel.StopButtonPressed += _stopHandler;
            _ioPanel.EmergencyStopButtonPressed += _emergencyPressedHandler;
            _ioPanel.ResetButtonPressed += _resetHandler;
            _ioPanel.EmergencyStopButtonReleased += _emergencyReleasedHandler;
            _logger.LogInformation("IoPanelStateTransitionHostedService 已挂载按钮状态桥接。");
        }

        /// <summary>
        /// 写入 IoPanel 按钮命令通道（满载时聚合告警）。
        /// </summary>
        /// <param name="command">IoPanel 状态命令。</param>
        private void TryEnqueueIoPanelCommand(IoPanelTransitionCommand command) {
            if (_ioPanelCommandChannel.Writer.TryWrite(command)) {
                return;
            }

            if (Volatile.Read(ref _ioPanelCommandChannelCompleted)) {
                _logger.LogDebug("IoPanel 命令通道已关闭，忽略命令 CommandType={CommandType}", command.CommandType);
                return;
            }

            var dropped = Interlocked.Increment(ref _droppedIoPanelCommandCount);
            var nowMs = Environment.TickCount64;
            var lastMs = Volatile.Read(ref _lastIoPanelCommandDropWarningElapsedMs);
            if (unchecked(nowMs - lastMs) >= 1000 &&
                Interlocked.CompareExchange(ref _lastIoPanelCommandDropWarningElapsedMs, nowMs, lastMs) == lastMs) {
                _logger.LogWarning(
                    "IoPanel 命令通道持续满载，已聚合丢弃 DroppedCount={DroppedCount} CommandType={CommandType}",
                    dropped,
                    command.CommandType);
            }
        }

        /// <summary>
        /// 按 FIFO 顺序消费 IoPanel 按钮命令。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task ConsumeIoPanelCommandChannelAsync(CancellationToken stoppingToken) {
            await foreach (var command in _ioPanelCommandChannel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false)) {
                try {
                    switch (command.CommandType) {
                        case IoPanelTransitionCommandType.StartPressed:
                            BeginStartupTransition(command.StoppingToken);
                            break;
                        case IoPanelTransitionCommandType.StopPressed:
                            CancelStartupTransition("StopButtonPressed");
                            await ChangeSystemStateSafeAsync(SystemState.Paused, command.StoppingToken, "StopButtonPressed").ConfigureAwait(false);
                            break;
                        case IoPanelTransitionCommandType.EmergencyPressed:
                            CancelStartupTransition("EmergencyStopButtonPressed");
                            await ChangeSystemStateSafeAsync(SystemState.EmergencyStop, command.StoppingToken, "EmergencyStopButtonPressed").ConfigureAwait(false);
                            break;
                        case IoPanelTransitionCommandType.ResetPressed:
                            CancelStartupTransition("ResetButtonPressed");
                            await ChangeSystemStateSafeAsync(SystemState.Booting, command.StoppingToken, "ResetButtonPressed").ConfigureAwait(false);
                            break;
                        case IoPanelTransitionCommandType.EmergencyReleased:
                            await ChangeSystemStateSafeAsync(SystemState.Ready, command.StoppingToken, "EmergencyStopButtonReleased").ConfigureAwait(false);
                            break;
                    }
                }
                catch (OperationCanceledException) {
                    // 正常取消路径。
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "处理 IoPanel 状态命令异常 CommandType={CommandType}", command.CommandType);
                }
            }
        }

        /// <summary>
        /// 取消订阅 IoPanel 所有按钮事件，并释放事件委托引用。
        /// </summary>
        private void UnsubscribeButtons() {
            if (_startHandler is not null) {
                _ioPanel.StartButtonPressed -= _startHandler;
                _startHandler = null;
            }

            if (_stopHandler is not null) {
                _ioPanel.StopButtonPressed -= _stopHandler;
                _stopHandler = null;
            }

            if (_emergencyPressedHandler is not null) {
                _ioPanel.EmergencyStopButtonPressed -= _emergencyPressedHandler;
                _emergencyPressedHandler = null;
            }

            if (_resetHandler is not null) {
                _ioPanel.ResetButtonPressed -= _resetHandler;
                _resetHandler = null;
            }

            if (_emergencyReleasedHandler is not null) {
                _ioPanel.EmergencyStopButtonReleased -= _emergencyReleasedHandler;
                _emergencyReleasedHandler = null;
            }
        }

        /// <summary>
        /// 启动“启动预警→运行”状态迁移流程。
        /// </summary>
        /// <param name="stoppingToken">服务停止令牌。</param>
        private void BeginStartupTransition(CancellationToken stoppingToken) {
            var startupTransitionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            lock (_startupTransitionSyncRoot) {
                CancelStartupTransitionUnsafe("RestartByStart");
                _startupTransitionCts = startupTransitionCts;
                _ = RunStartupTransitionAsync(startupTransitionCts);
            }
        }

        /// <summary>
        /// 执行启动预警阶段并在到时后切换为运行态。
        /// </summary>
        /// <param name="startupTransitionCts">本次启动迁移的取消源。</param>
        /// <returns>异步任务。</returns>
        private async Task RunStartupTransitionAsync(CancellationTokenSource startupTransitionCts) {
            try {
                // 步骤1：进入启动预警状态。
                await ChangeSystemStateSafeAsync(
                    SystemState.StartupWarning,
                    startupTransitionCts.Token,
                    "StartButtonPressed.StartupWarning").ConfigureAwait(false);

                await Task.Delay(_startupWarningDuration, startupTransitionCts.Token).ConfigureAwait(false);

                // 步骤2：启动预警结束后，先进入环线预热状态。
                await ChangeSystemStateSafeAsync(
                    SystemState.LoopTrackWarmingUp,
                    startupTransitionCts.Token,
                    "StartButtonPressed.LoopTrackWarmingUp").ConfigureAwait(false);

                // 步骤3：等待环线满足正式运行条件。
                await WaitLoopTrackReadyForRunningAsync(startupTransitionCts.Token).ConfigureAwait(false);

                if (startupTransitionCts.Token.IsCancellationRequested) {
                    return;
                }

                // 步骤4：环线建环且稳速完成后，正式进入运行态。
                await ChangeSystemStateSafeAsync(
                    SystemState.Running,
                    startupTransitionCts.Token,
                    "StartButtonPressed.Running").ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (startupTransitionCts.IsCancellationRequested) {
                _logger.LogInformation("启动预警阶段已取消，阻止切换到 Running。");
            }
            finally {
                lock (_startupTransitionSyncRoot) {
                    if (ReferenceEquals(_startupTransitionCts, startupTransitionCts)) {
                        _startupTransitionCts = null;
                    }
                }

                startupTransitionCts.Dispose();
            }
        }

        /// <summary>
        /// 取消当前启动预警迁移流程。
        /// </summary>
        /// <param name="reason">取消原因。</param>
        private void CancelStartupTransition(string reason) {
            lock (_startupTransitionSyncRoot) {
                CancelStartupTransitionUnsafe(reason);
            }
        }

        /// <summary>
        /// 在已持有同步锁时取消当前启动预警迁移流程。
        /// </summary>
        /// <param name="reason">取消原因。</param>
        private void CancelStartupTransitionUnsafe(string reason) {
            if (_startupTransitionCts is null) {
                return;
            }

            _logger.LogInformation("取消启动预警阶段：Reason={Reason}。", reason);
            _startupTransitionCts.Cancel();
        }

        private Task ChangeSystemStateSafeAsync(
            SystemState targetState,
            CancellationToken stoppingToken,
            string sourceEvent) {
            return _safeExecutor.ExecuteAsync(
                async token => {
                    var changed = await _systemStateManager.ChangeStateAsync(targetState, token).ConfigureAwait(false);
                    if (!changed) {
                        _logger.LogWarning("IoPanel 状态桥接未生效：Event={Event}, TargetState={TargetState}。", sourceEvent, targetState);
                    }
                },
                $"IoPanelStateTransitionHostedService.{sourceEvent}",
                stoppingToken);
        }

        /// <summary>
        /// 等待环线达到正式运行条件：已建环且已稳速。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task WaitLoopTrackReadyForRunningAsync(CancellationToken cancellationToken) {
            // 步骤1：等待环轨管理器就绪。
            var loopTrackManager = await WaitLoopTrackManagerReadyAsync(cancellationToken).ConfigureAwait(false);

            // 步骤2：若小车尚未建环，则先等待建环完成。
            if (!_carrierManager.IsRingBuilt) {
                _logger.LogInformation("环线预热阶段：当前尚未建环，开始等待建环完成。");

                while (!_carrierManager.IsRingBuilt) {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogInformation("环线预热阶段：已检测到小车建环完成。");
            }
            else {
                _logger.LogInformation("环线预热阶段：当前已建环，跳过建环等待。");
            }

            // 步骤3：等待环轨稳速完成。
            if (loopTrackManager.StabilizationStatus != LoopTrackStabilizationStatus.Stabilized) {
                _logger.LogInformation(
                    "环线预热阶段：当前稳速状态为 {StabilizationStatus}，开始等待稳速完成。",
                    loopTrackManager.StabilizationStatus);

                while (loopTrackManager.StabilizationStatus != LoopTrackStabilizationStatus.Stabilized) {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogInformation("环线预热阶段：已检测到环轨稳速完成。");
            }
            else {
                _logger.LogInformation("环线预热阶段：当前已稳速，跳过稳速等待。");
            }
        }

        /// <summary>
        /// 等待环轨管理器就绪。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>环轨管理器实例。</returns>
        private async Task<ILoopTrackManager> WaitLoopTrackManagerReadyAsync(CancellationToken cancellationToken) {
            while (true) {
                cancellationToken.ThrowIfCancellationRequested();

                var manager = _loopTrackAccessor.Manager;
                if (manager is not null) {
                    return manager;
                }

                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
