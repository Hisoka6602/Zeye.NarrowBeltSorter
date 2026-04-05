using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.System;
using Zeye.NarrowBeltSorter.Core.Manager.System;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine {
    /// <summary>
    /// 固定系统状态测试桩，用于在单元测试中替代真实的 ISystemStateManager。
    /// </summary>
    internal sealed class StubSystemStateManager(SystemState currentState) : ISystemStateManager {
        /// <inheritdoc />
        public SystemState CurrentState { get; private set; } = currentState;

        /// <inheritdoc />
        public event EventHandler<StateChangeEventArgs>? StateChanged;

        /// <inheritdoc />
        public Task<bool> ChangeStateAsync(SystemState targetState, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            var oldState = CurrentState;
            CurrentState = targetState;
            StateChanged?.Invoke(this, new StateChangeEventArgs(oldState, targetState, DateTime.Now));
            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public void Dispose() {
        }
    }
}
