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
        public List<SystemState> ChangedStates { get; } = [];

        /// <inheritdoc />
        public event EventHandler<StateChangeEventArgs>? StateChanged;

        /// <inheritdoc />
        public Task<bool> ChangeStateAsync(SystemState targetState, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            var oldState = CurrentState;
            CurrentState = targetState;
            ChangedStates.Add(targetState);
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
