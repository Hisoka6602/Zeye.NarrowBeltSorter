using Zeye.NarrowBeltSorter.Core.Enums.Sorting;

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
        /// 包裹成熟时间起始来源默认值。
        /// </summary>
        public const ParcelMatureStartSource DefaultParcelMatureStartSource = ParcelMatureStartSource.ParcelCreateSensor;

        /// <summary>
        /// 包裹从创建到进入待装车队列的成熟延迟（毫秒）。
        /// </summary>
        public int ParcelMatureDelayMs { get; set; } = DefaultParcelMatureDelayMs;

        /// <summary>
        /// 包裹成熟时间起始来源（可选值：ParcelCreateSensor/LoadingTriggerSensor）。
        /// </summary>
        public ParcelMatureStartSource ParcelMatureStartSource { get; set; } = DefaultParcelMatureStartSource;

        /// <summary>
        /// 当起始来源为 LoadingTriggerSensor 且尚未接收到上车触发源时，是否回退为创建包裹触发源（取值：true/false）。
        /// </summary>
        public bool EnableFallbackToParcelCreateWhenLoadingTriggerMissing { get; set; }

        /// <summary>
        /// 格口开门到关门的间隔时间（毫秒）。
        /// </summary>
        public int ChuteOpenCloseIntervalMs { get; set; } = DefaultChuteOpenCloseIntervalMs;
    }
}
