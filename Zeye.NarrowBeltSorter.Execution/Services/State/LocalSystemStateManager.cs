using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.System;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Zeye.NarrowBeltSorter.Execution.Services.State {
    /// <summary>
    /// 本地系统状态管理器实现。
    /// </summary>
    public sealed class LocalSystemStateManager : ISystemStateManager {
        private readonly ILogger<LocalSystemStateManager> _logger;
        private readonly SafeExecutor _safeExecutor;
        private readonly object _stateLock = new();
        private bool _disposed;

        /// <summary>
        /// 初始化本地系统状态管理器。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        public LocalSystemStateManager(ILogger<LocalSystemStateManager> logger, SafeExecutor safeExecutor) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
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

            _safeExecutor.PublishEventAsync(
                StateChanged,
                this,
                eventArgs.Value,
                "LocalSystemStateManager.StateChanged");

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
                _logger.LogError("LocalSystemStateManager 已释放，拒绝继续操作。");
                throw new ObjectDisposedException(nameof(LocalSystemStateManager));
            }
        }
    }
}
