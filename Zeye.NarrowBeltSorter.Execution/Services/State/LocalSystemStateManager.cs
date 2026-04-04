using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.IoPanel;
using Zeye.NarrowBeltSorter.Core.Manager.IoPanel;
using Zeye.NarrowBeltSorter.Core.Events.System;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Zeye.NarrowBeltSorter.Execution.Services.State {
    /// <summary>
    /// 本地系统状态管理器实现。
    /// </summary>
    public sealed class LocalSystemStateManager : ISystemStateManager {
        /// <summary>运行态切换到检修态前的停机等待时长（毫秒），用于保障运动部件完成停稳。</summary>
        private const int RunningToMaintenancePauseDelayMilliseconds = 300;
        private readonly ILogger<LocalSystemStateManager> _logger;
        private readonly SafeExecutor _safeExecutor;
        private readonly object _stateLock = new();
        private readonly HashSet<string> _activeEmergencyStopPointIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly IIoPanel? _ioPanel;
        private EventHandler<IoPanelButtonPressedEventArgs>? _emergencyPressedHandler;
        private EventHandler<IoPanelButtonReleasedEventArgs>? _emergencyReleasedHandler;
        private bool _disposed;

        /// <summary>
        /// 初始化本地系统状态管理器。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <param name="ioPanel">IoPanel 管理器（可选）。</param>
        public LocalSystemStateManager(ILogger<LocalSystemStateManager> logger, SafeExecutor safeExecutor, IIoPanel? ioPanel = null) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _ioPanel = ioPanel;
            SubscribeIoPanelEvents();
        }

        /// <summary>
        /// 获取当前系统状态。
        /// </summary>
        public SystemState CurrentState { get; private set; } = SystemState.Ready;

        /// <summary>
        /// 系统状态变更事件。
        /// </summary>
        public event EventHandler<StateChangeEventArgs>? StateChanged;

        /// <summary>
        /// 变更系统状态并发布事件。
        /// </summary>
        /// <param name="targetState">目标状态。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否变更成功。</returns>
        public async Task<bool> ChangeStateAsync(SystemState targetState, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            // 步骤1：运行态切换到检修态必须先切到暂停，等待 300ms 后再切到检修态。
            if (targetState == SystemState.Maintenance &&
                IsCurrentState(SystemState.Running)) {
                var paused = TryPauseForMaintenanceTransition(out var pauseNoOp, out var pauseEventArgs, out var pauseRejectReason);
                if (!paused) {
                    _logger.LogWarning(
                        "系统状态切换被驳回：CurrentState={CurrentState}, TargetState={TargetState}, Reason={Reason}。",
                        CurrentState,
                        SystemState.Paused,
                        pauseRejectReason);
                    return false;
                }

                if (!pauseNoOp) {
                    PublishStateChangedEvent(pauseEventArgs);
                }
                try {
                    await Task.Delay(RunningToMaintenancePauseDelayMilliseconds, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    _logger.LogInformation("运行态切换检修态在停机等待阶段被取消。");
                    return false;
                }

                if (!CanContinueMaintenanceTransition(out var transitionRejectReason)) {
                    _logger.LogWarning(
                        "系统状态切换被驳回：CurrentState={CurrentState}, TargetState={TargetState}, Reason={Reason}。",
                        CurrentState,
                        targetState,
                        transitionRejectReason);
                    return false;
                }
            }

            // 步骤2：执行目标状态切换并发布事件。
            var changed = TryChangeStateCore(targetState, out var noOp, out var eventArgs, out var rejectReason);
            if (!changed) {
                _logger.LogWarning(
                    "系统状态切换被驳回：CurrentState={CurrentState}, TargetState={TargetState}, Reason={Reason}。",
                    CurrentState,
                    targetState,
                    rejectReason);
                return false;
            }

            if (noOp) {
                return true;
            }

            PublishStateChangedEvent(eventArgs);
            return true;
        }

        /// <summary>
        /// 释放状态管理器资源。
        /// </summary>
        public void Dispose() {
            lock (_stateLock) {
                _disposed = true;
            }

            UnsubscribeIoPanelEvents();
        }

        /// <summary>
        /// 校验是否已释放。
        /// </summary>
        private void ThrowIfDisposed() {
            if (_disposed) {
                _logger.LogError("LocalSystemStateManager 已释放，拒绝继续操作。");
                throw new ObjectDisposedException(nameof(LocalSystemStateManager));
            }
        }

        /// <summary>
        /// 核心状态切换实现（含驳回规则校验）。
        /// </summary>
        /// <param name="targetState">目标状态。</param>
        /// <param name="noOp">是否为无变化切换。</param>
        /// <param name="eventArgs">状态变更事件参数。</param>
        /// <param name="rejectReason">驳回原因。</param>
        /// <returns>是否允许切换。</returns>
        private bool TryChangeStateCore(
            SystemState targetState,
            out bool noOp,
            out StateChangeEventArgs eventArgs,
            out string rejectReason) {
            lock (_stateLock) {
                return TryChangeStateCoreUnderLock(targetState, out noOp, out eventArgs, out rejectReason);
            }
        }

        /// <summary>
        /// 发布系统状态变更事件。
        /// </summary>
        /// <param name="eventArgs">事件参数。</param>
        private void PublishStateChangedEvent(StateChangeEventArgs eventArgs) {
            _safeExecutor.PublishEventAsync(
                StateChanged,
                this,
                eventArgs,
                "LocalSystemStateManager.StateChanged");
        }

        /// <summary>
        /// 订阅 IoPanel 急停按钮事件。
        /// </summary>
        private void SubscribeIoPanelEvents() {
            if (_ioPanel is null) {
                return;
            }

            _emergencyPressedHandler = (_, args) => QueueEmergencyEventHandling(
                () => OnEmergencyStopPressed(args),
                "LocalSystemStateManager.EmergencyStopPressed");
            _emergencyReleasedHandler = (_, args) => QueueEmergencyEventHandling(
                () => OnEmergencyStopReleased(args),
                "LocalSystemStateManager.EmergencyStopReleased");
            _ioPanel.EmergencyStopButtonPressed += _emergencyPressedHandler;
            _ioPanel.EmergencyStopButtonReleased += _emergencyReleasedHandler;
        }

        /// <summary>
        /// 取消订阅 IoPanel 急停按钮事件。
        /// </summary>
        private void UnsubscribeIoPanelEvents() {
            if (_ioPanel is null) {
                return;
            }

            if (_emergencyPressedHandler is not null) {
                _ioPanel.EmergencyStopButtonPressed -= _emergencyPressedHandler;
                _emergencyPressedHandler = null;
            }

            if (_emergencyReleasedHandler is not null) {
                _ioPanel.EmergencyStopButtonReleased -= _emergencyReleasedHandler;
                _emergencyReleasedHandler = null;
            }
        }

        /// <summary>
        /// 处理急停按钮按下，登记未解除急停点位。
        /// </summary>
        /// <param name="args">按钮按下事件参数。</param>
        private void OnEmergencyStopPressed(IoPanelButtonPressedEventArgs args) {
            var activeCount = 0;
            lock (_stateLock) {
                if (_disposed) {
                    return;
                }

                _activeEmergencyStopPointIds.Add(args.PointId);
                activeCount = _activeEmergencyStopPointIds.Count;
            }

            _logger.LogInformation(
                "登记急停按下：PointId={PointId}, ActiveEmergencyStopCount={ActiveCount}。",
                args.PointId,
                activeCount);
        }

        /// <summary>
        /// 处理急停按钮释放，移除已解除急停点位。
        /// </summary>
        /// <param name="args">按钮释放事件参数。</param>
        private void OnEmergencyStopReleased(IoPanelButtonReleasedEventArgs args) {
            var activeCount = 0;
            lock (_stateLock) {
                if (_disposed) {
                    return;
                }

                _activeEmergencyStopPointIds.Remove(args.PointId);
                activeCount = _activeEmergencyStopPointIds.Count;
            }

            _logger.LogInformation(
                "登记急停释放：PointId={PointId}, ActiveEmergencyStopCount={ActiveCount}。",
                args.PointId,
                activeCount);
        }

        /// <summary>
        /// 校验当前状态是否与目标状态一致。
        /// </summary>
        /// <param name="expectedState">期望状态。</param>
        /// <returns>是否一致。</returns>
        private bool IsCurrentState(SystemState expectedState) {
            lock (_stateLock) {
                ThrowIfDisposed();
                return CurrentState == expectedState;
            }
        }

        /// <summary>
        /// 尝试执行“运行态到检修态”的首段过渡（运行态到暂停态）。
        /// </summary>
        /// <param name="pauseNoOp">暂停切换是否无变化。</param>
        /// <param name="pauseEventArgs">暂停切换事件参数。</param>
        /// <param name="rejectReason">驳回原因。</param>
        /// <returns>是否允许进入过渡。</returns>
        private bool TryPauseForMaintenanceTransition(
            out bool pauseNoOp,
            out StateChangeEventArgs pauseEventArgs,
            out string rejectReason) {
            lock (_stateLock) {
                ThrowIfDisposed();
                if (CurrentState != SystemState.Running) {
                    pauseNoOp = false;
                    pauseEventArgs = default;
                    rejectReason = $"运行到检修过渡中断，当前状态已变为{CurrentState}";
                    return false;
                }

                return TryChangeStateCoreUnderLock(SystemState.Paused, out pauseNoOp, out pauseEventArgs, out rejectReason);
            }
        }

        /// <summary>
        /// 校验运行态到检修态等待完成后是否可继续切换。
        /// </summary>
        /// <param name="rejectReason">驳回原因。</param>
        /// <returns>是否可以继续。</returns>
        private bool CanContinueMaintenanceTransition(out string rejectReason) {
            lock (_stateLock) {
                ThrowIfDisposed();
                if (CurrentState != SystemState.Paused) {
                    rejectReason = $"运行到检修过渡等待结束后状态已变更为{CurrentState}";
                    return false;
                }

                rejectReason = string.Empty;
                return true;
            }
        }

        /// <summary>
        /// 在锁内执行状态切换校验与状态写入。
        /// </summary>
        /// <param name="targetState">目标状态。</param>
        /// <param name="noOp">是否为无变化切换。</param>
        /// <param name="eventArgs">状态变更事件参数。</param>
        /// <param name="rejectReason">驳回原因。</param>
        /// <returns>是否允许切换。</returns>
        private bool TryChangeStateCoreUnderLock(
            SystemState targetState,
            out bool noOp,
            out StateChangeEventArgs eventArgs,
            out string rejectReason) {
            ThrowIfDisposed();
            if (CurrentState == targetState) {
                noOp = true;
                eventArgs = default;
                rejectReason = string.Empty;
                return true;
            }

            if (_activeEmergencyStopPointIds.Count > 0 && targetState != SystemState.EmergencyStop) {
                noOp = false;
                eventArgs = default;
                rejectReason = $"仍存在未解除急停按钮数量={_activeEmergencyStopPointIds.Count}";
                return false;
            }

            if (CurrentState == SystemState.Maintenance &&
                targetState != SystemState.Paused &&
                targetState != SystemState.EmergencyStop) {
                noOp = false;
                eventArgs = default;
                rejectReason = "检修状态仅允许切换到暂停或急停";
                return false;
            }

            var oldState = CurrentState;
            CurrentState = targetState;
            eventArgs = new StateChangeEventArgs(oldState, targetState, DateTime.Now);
            noOp = false;
            rejectReason = string.Empty;
            return true;
        }

        /// <summary>
        /// 将急停按钮回调投递到隔离执行通道，避免阻塞 IoPanel 发布链路。
        /// </summary>
        /// <param name="action">待执行回调。</param>
        /// <param name="operationName">操作名称。</param>
        private void QueueEmergencyEventHandling(Action action, string operationName) {
            ThreadPool.UnsafeQueueUserWorkItem(
                static state => {
                    var context = ((LocalSystemStateManager manager, Action action, string operationName))state!;
                    context.manager._safeExecutor.Execute(context.action, context.operationName);
                },
                (this, action, operationName),
                preferLocal: false);
        }
    }
}
