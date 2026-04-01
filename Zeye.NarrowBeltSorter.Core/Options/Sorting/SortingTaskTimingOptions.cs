namespace Zeye.NarrowBeltSorter.Core.Options.Sorting {

    /// <summary>
    /// 分拣任务时序配置。
    /// </summary>
    public sealed class SortingTaskTimingOptions {
        /// <summary>
        /// 包裹从创建到进入待装车队列的成熟延迟默认值（毫秒）。
        /// </summary>
        public const int DefaultParcelMatureDelayMs = 2500;

        /// <summary>
        /// 格口开门到关门的间隔默认值（毫秒）。
        /// </summary>
        public const int DefaultChuteOpenCloseIntervalMs = 350;

        /// <summary>
        /// 包裹从创建到进入待装车队列的成熟延迟（毫秒）。
        /// </summary>
        public int ParcelMatureDelayMs { get; set; } = DefaultParcelMatureDelayMs;

        /// <summary>
        /// 格口开门到关门的间隔时间（毫秒）。
        /// </summary>
        public int ChuteOpenCloseIntervalMs { get; set; } = DefaultChuteOpenCloseIntervalMs;
    }
}
