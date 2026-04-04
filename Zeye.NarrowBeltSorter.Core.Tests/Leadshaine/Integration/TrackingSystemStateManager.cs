using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.System;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Utilities;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Integration {
    /// <summary>
    /// 可追踪历史状态变更的系统状态管理器测试桩。
    /// </summary>
    internal sealed class TrackingSystemStateManager : ISystemStateManager {
        private readonly SafeExecutor _eventExecutor;
        private readonly object _historyLock = new();
        private readonly List<SystemState> _changedStates = [];

        /// <summary>
        /// 初始化可追踪历史状态变更的系统状态管理器测试桩。
        /// </summary>
        /// <param name="safeExecutor">统一安全执行器。</param>
        public TrackingSystemStateManager(SafeExecutor safeExecutor) {
            _eventExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
        }
        /// <inheritdoc />
        public SystemState CurrentState { get; private set; } = SystemState.Ready;

        /// <summary>
        /// 已变更过的系统状态历史列表。
        /// </summary>
        public IReadOnlyList<SystemState> GetChangedStatesSnapshot() {
            lock (_historyLock) {
                return _changedStates.ToArray();
            }
        }

        /// <summary>
        /// 清空历史状态记录。
        /// </summary>
        public void ClearHistory() {
            lock (_historyLock) {
                _changedStates.Clear();
            }
        }

        /// <inheritdoc />
        public event EventHandler<StateChangeEventArgs>? StateChanged;

        /// <inheritdoc />
        public Task<bool> ChangeStateAsync(SystemState targetState, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            var oldState = CurrentState;
            CurrentState = targetState;
            lock (_historyLock) {
                _changedStates.Add(targetState);
            }
            _eventExecutor.PublishEventAsync(
                StateChanged,
                this,
                new StateChangeEventArgs(oldState, targetState, DateTime.Now),
                "TrackingSystemStateManager.StateChanged");
            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public void Dispose() {
        }
    }
}
