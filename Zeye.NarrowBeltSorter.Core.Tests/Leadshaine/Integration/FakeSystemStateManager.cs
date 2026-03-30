using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.System;
using Zeye.NarrowBeltSorter.Core.Manager.System;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Integration {
    /// <summary>
    /// 系统状态管理器测试桩。
    /// </summary>
    public sealed class FakeSystemStateManager : ISystemStateManager {
        /// <inheritdoc />
        public SystemState CurrentState { get; private set; } = SystemState.Ready;

        /// <inheritdoc />
        public event EventHandler<StateChangeEventArgs>? StateChanged;

        /// <inheritdoc />
        public Task<bool> ChangeStateAsync(SystemState targetState, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            var old = CurrentState;
            CurrentState = targetState;
            StateChanged?.Invoke(this, new StateChangeEventArgs(old, targetState, DateTime.Now));
            return Task.FromResult(true);
        }

        /// <summary>
        /// 主动触发状态变更事件。
        /// </summary>
        /// <param name="targetState">目标状态。</param>
        public void RaiseStateChanged(SystemState targetState) {
            var old = CurrentState;
            CurrentState = targetState;
            StateChanged?.Invoke(this, new StateChangeEventArgs(old, targetState, DateTime.Now));
        }

        /// <inheritdoc />
        public void Dispose() {
        }
    }
}
