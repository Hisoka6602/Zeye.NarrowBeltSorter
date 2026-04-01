using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// 测试用环轨管理器访问器存根，允许直接设置 Manager 属性。
    /// </summary>
    internal sealed class FakeLoopTrackManagerAccessor : ILoopTrackManagerAccessor {

        private ILoopTrackManager? _manager;

        /// <summary>
        /// 当前管理器实例。
        /// </summary>
        public ILoopTrackManager? Manager => _manager;

        /// <summary>
        /// 管理器实例变更事件。
        /// </summary>
        public event EventHandler<ILoopTrackManager?>? ManagerChanged;

        /// <summary>
        /// 设置当前管理器实例并触发变更通知。
        /// </summary>
        /// <param name="manager">新管理器实例；传 null 表示清空。</param>
        public void SetManager(ILoopTrackManager? manager) {
            _manager = manager;
            ManagerChanged?.Invoke(this, manager);
        }
    }
}
