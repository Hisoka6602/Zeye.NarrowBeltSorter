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
        /// 尝试读取当前环形轨道实时速度快照（单位：mm/s）。
        /// </summary>
        /// <param name="realTimeSpeedMmps">实时速度快照。</param>
        /// <returns>当管理器可用且读取成功时返回 true；否则返回 false。</returns>
        public bool TryGetRealTimeSpeedMmps(out decimal realTimeSpeedMmps) {
            var manager = _manager;
            if (manager is null) {
                realTimeSpeedMmps = default;
                return false;
            }

            realTimeSpeedMmps = manager.RealTimeSpeedMmps;
            return true;
        }

        /// <summary>
        /// 设置当前管理器实例并触发变更通知。
        /// 注意：测试桩此处直接同步调用，生产实现通过 <c>SafeExecutor.PublishEventAsync</c> 非阻塞并行发布；
        /// 若需验证事件发布的并发隔离行为，请在集成测试中使用生产实现。
        /// </summary>
        /// <param name="manager">新管理器实例；传 null 表示清空。</param>
        public void SetManager(ILoopTrackManager? manager) {
            _manager = manager;
            ManagerChanged?.Invoke(this, manager);
        }
    }
}
