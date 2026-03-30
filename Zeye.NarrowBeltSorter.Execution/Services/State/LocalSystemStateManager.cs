using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.System;
using Zeye.NarrowBeltSorter.Core.Manager.System;

namespace Zeye.NarrowBeltSorter.Execution.Services.State {
    /// <summary>
    /// 本地系统状态管理器实现。
    /// </summary>
    public sealed class LocalSystemStateManager : ISystemStateManager {
        private readonly object _stateLock = new();
        private bool _disposed;

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
        public Task<bool> ChangeStateAsync(SystemState targetState, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            StateChangeEventArgs? eventArgs = null;
            lock (_stateLock) {
                ThrowIfDisposed();
                if (CurrentState == targetState) {
                    return Task.FromResult(true);
                }

                var oldState = CurrentState;
                CurrentState = targetState;
                eventArgs = new StateChangeEventArgs(oldState, targetState, DateTime.Now);
            }

            StateChanged?.Invoke(this, eventArgs.Value);
            return Task.FromResult(true);
        }

        /// <summary>
        /// 释放状态管理器资源。
        /// </summary>
        public void Dispose() {
            lock (_stateLock) {
                _disposed = true;
            }
        }

        /// <summary>
        /// 校验是否已释放。
        /// </summary>
        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(LocalSystemStateManager));
            }
        }
    }
}
