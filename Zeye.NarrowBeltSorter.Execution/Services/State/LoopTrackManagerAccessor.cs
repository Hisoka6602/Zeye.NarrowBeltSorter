using System.Threading;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Utilities;

namespace Zeye.NarrowBeltSorter.Execution.Services.State {

    /// <summary>
    /// 环形轨道管理器访问器实现。
    /// 托管服务通过 <see cref="SetManager"/> 注册或清空当前管理器引用；
    /// 其他服务通过 <see cref="Manager"/> 属性读取实例，并通过 <see cref="ManagerChanged"/> 事件感知实例变更时机，
    /// 以便在管理器可用时及时完成事件订阅。
    /// </summary>
    public sealed class LoopTrackManagerAccessor : ILoopTrackManagerAccessor {

        private volatile ILoopTrackManager? _manager;

        private readonly object _lock = new();

        private readonly SafeExecutor _safeExecutor;

        /// <summary>变更版本号，每次 <see cref="SetManager"/> 调用时在锁内递增，用于在投递前丢弃已被覆盖的过期异步通知。</summary>
        private int _version;

        /// <summary>
        /// 初始化环形轨道管理器访问器实例。
        /// </summary>
        /// <param name="safeExecutor">统一安全执行器，用于非阻塞并行发布事件。</param>
        public LoopTrackManagerAccessor(SafeExecutor safeExecutor) {
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
        }

        /// <summary>
        /// 当前管理器实例；托管服务未启动或已停止时为 null。
        /// </summary>
        public ILoopTrackManager? Manager => _manager;

        /// <summary>
        /// 管理器实例变更事件；实例设置或清空时触发，参数为新实例（null 表示已清空）。
        /// </summary>
        public event EventHandler<ILoopTrackManager?>? ManagerChanged;

        /// <summary>
        /// 设置当前管理器实例并触发变更通知；仅当新实例与旧实例不同时才触发事件。
        /// 采用"最后写入胜出"策略：若在 ThreadPool 投递到达前已有更新的调用写入，则丢弃该过期投递，
        /// 确保订阅者不会因乱序到达而错误地处理旧状态。
        /// </summary>
        /// <param name="manager">新管理器实例；传 null 表示清空。</param>
        public void SetManager(ILoopTrackManager? manager) {
            // 步骤1：原子交换旧实例并递增版本号，仅在引用变更时才触发事件，避免冗余通知。
            ILoopTrackManager? old;
            int capturedVersion;
            lock (_lock) {
                old = _manager;
                _manager = manager;
                capturedVersion = ++_version;
            }

            // 步骤2：引用发生变化才投递变更通知；投递时先核对版本号，版本落后则代表已被更新的调用覆盖，丢弃本次过期投递。
            if (!ReferenceEquals(old, manager)) {
                ThreadPool.UnsafeQueueUserWorkItem(
                    static state => {
                        var (self, v, mgr, executor) = ((LoopTrackManagerAccessor, int, ILoopTrackManager?, SafeExecutor))state!;
                        // 版本不一致表示已有更新的 SetManager 写入，丢弃本次过期投递，避免乱序导致订阅者状态回退。
                        if (Volatile.Read(ref self._version) != v) {
                            return;
                        }
                        executor.PublishEventAsync(self.ManagerChanged, self, mgr, "LoopTrackManagerAccessor.ManagerChanged");
                    },
                    (this, capturedVersion, manager, _safeExecutor),
                    preferLocal: false);
            }
        }
    }
}
