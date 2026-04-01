using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;

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
        /// </summary>
        /// <param name="manager">新管理器实例；传 null 表示清空。</param>
        public void SetManager(ILoopTrackManager? manager) {
            // 步骤1：原子交换旧实例，仅在引用变更时才触发事件，避免冗余通知。
            ILoopTrackManager? old;
            lock (_lock) {
                old = _manager;
                _manager = manager;
            }

            // 步骤2：引用发生变化才触发变更事件，防止无效订阅/退订。
            if (!ReferenceEquals(old, manager)) {
                ManagerChanged?.Invoke(this, manager);
            }
        }
    }
}
