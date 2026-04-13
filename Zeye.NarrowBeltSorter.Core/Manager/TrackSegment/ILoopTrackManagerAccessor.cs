namespace Zeye.NarrowBeltSorter.Core.Manager.TrackSegment {

    /// <summary>
    /// 环形轨道管理器访问器接口。
    /// 提供对当前管理器实例的跨服务读取、变更通知与生命周期注册能力；
    /// 管理器的生命周期由托管服务统一控制，其他服务仅读取 <see cref="Manager"/> 并订阅 <see cref="ManagerChanged"/> 事件。
    /// </summary>
    public interface ILoopTrackManagerAccessor {

        /// <summary>
        /// 当前管理器实例；托管服务未启动或已停止时为 null。
        /// </summary>
        ILoopTrackManager? Manager { get; }

        /// <summary>
        /// 管理器实例变更事件；托管服务创建或销毁管理器时触发，参数为新实例（null 表示已清空）。
        /// </summary>
        event EventHandler<ILoopTrackManager?>? ManagerChanged;

        /// <summary>
        /// 设置当前管理器实例并触发变更通知；仅供托管服务调用，其他服务不应直接调用此方法。
        /// </summary>
        /// <param name="manager">新管理器实例；传 null 表示清空。</param>
        void SetManager(ILoopTrackManager? manager);

        /// <summary>
        /// 尝试读取当前环形轨道实时速度快照（单位：mm/s）。
        /// </summary>
        /// <param name="realTimeSpeedMmps">实时速度快照。</param>
        /// <returns>当管理器可用且读取成功时返回 true；否则返回 false。</returns>
        bool TryGetRealTimeSpeedMmps(out decimal realTimeSpeedMmps);
    }
}
