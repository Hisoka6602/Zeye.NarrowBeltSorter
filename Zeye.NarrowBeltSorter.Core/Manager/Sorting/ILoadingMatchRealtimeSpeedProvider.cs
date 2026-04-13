namespace Zeye.NarrowBeltSorter.Core.Manager.Sorting {

    /// <summary>
    /// 上车匹配实时速度提供接口。
    /// 上车补偿计算仅依赖此接口获取环线速度，不直接依赖具体轨道管理器类型，保持层级边界清晰。
    /// </summary>
    public interface ILoadingMatchRealtimeSpeedProvider {

        /// <summary>
        /// 尝试获取当前环线实时速度快照（单位：mm/s）。
        /// </summary>
        /// <param name="speedMmps">实时速度快照；不可用时为 0。</param>
        /// <returns>管理器可用且速度读取成功时返回 true；否则返回 false。</returns>
        bool TryGetSpeedMmps(out decimal speedMmps);
    }
}
