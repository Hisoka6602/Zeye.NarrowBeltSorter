using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.System;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Integration {
    /// <summary>
    /// 系统状态管理器测试桩。
    /// </summary>
    public sealed class FakeSystemStateManager : ISystemStateManager {
        private static readonly SafeExecutor EventExecutor = new(NullLogger<SafeExecutor>.Instance);
        private int _subscriberCount;
        private readonly ManualResetEventSlim _subscriberReady = new(false);
        private EventHandler<StateChangeEventArgs>? _stateChanged;

        /// <inheritdoc />
        public SystemState CurrentState { get; private set; } = SystemState.Ready;

        /// <inheritdoc />
        public event EventHandler<StateChangeEventArgs>? StateChanged {
            add {
                _stateChanged += value;
                Interlocked.Increment(ref _subscriberCount);
                _subscriberReady.Set();
            }
            remove {
                _stateChanged -= value;
                Interlocked.Decrement(ref _subscriberCount);
                if (Volatile.Read(ref _subscriberCount) <= 0) {
                    _subscriberReady.Reset();
                }
            }
        }

        /// <inheritdoc />
        public Task<bool> ChangeStateAsync(SystemState targetState, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            var old = CurrentState;
            CurrentState = targetState;
            EventExecutor.PublishEventAsync(
                _stateChanged,
                this,
                new StateChangeEventArgs(old, targetState, DateTime.Now),
                "FakeSystemStateManager.StateChanged");
            return Task.FromResult(true);
        }

        /// <summary>
        /// 主动触发状态变更事件。
        /// </summary>
        /// <param name="targetState">目标状态。</param>
        public void RaiseStateChanged(SystemState targetState) {
            var old = CurrentState;
            CurrentState = targetState;
            EventExecutor.PublishEventAsync(
                _stateChanged,
                this,
                new StateChangeEventArgs(old, targetState, DateTime.Now),
                "FakeSystemStateManager.StateChanged");
        }

        /// <summary>
        /// 等待至少一个订阅者已挂载。
        /// </summary>
        /// <param name="timeoutMs">超时时间（毫秒）。</param>
        /// <returns>是否在超时内检测到订阅者。</returns>
        public bool WaitForSubscriber(int timeoutMs) {
            return _subscriberReady.Wait(timeoutMs);
        }

        /// <inheritdoc />
        public void Dispose() {
        }
    }
}
