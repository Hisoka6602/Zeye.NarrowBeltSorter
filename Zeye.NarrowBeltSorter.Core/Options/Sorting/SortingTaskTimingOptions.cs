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
        /// 上车触发相对创建触发允许的最大滞后窗口默认值（毫秒）。
        /// </summary>
        public const int DefaultLoadingTriggerLagWindowMs = 5000;

        /// <summary>
        /// 链路阶段耗时告警阈值默认值（毫秒）。
        /// </summary>
        public const int DefaultParcelChainAlertThresholdMs = 3000;

        /// <summary>
        /// 在 LoadingTriggerSensor 模式下等待上车触发绑定的最长超时时间默认值（毫秒）。
        /// </summary>
        public const int DefaultLoadingTriggerWaitTimeoutMs = 8000;

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
        /// 上车触发相对创建触发允许的最大滞后窗口（单位：毫秒，建议范围：1~30000）。
        /// </summary>
        public int LoadingTriggerLagWindowMs { get; set; } = DefaultLoadingTriggerLagWindowMs;

        /// <summary>
        /// 格口开门到关门的间隔时间（毫秒）。
        /// </summary>
        public int ChuteOpenCloseIntervalMs { get; set; } = DefaultChuteOpenCloseIntervalMs;

        /// <summary>
        /// 链路阶段耗时告警阈值（单位：毫秒，建议范围：500~30000）。
        /// 任意链路阶段（上车触发→上车成功、上车成功→到达格口）耗时超过此阈值时输出告警日志并附带上下文信息。
        /// </summary>
        public int ParcelChainAlertThresholdMs { get; set; } = DefaultParcelChainAlertThresholdMs;

        /// <summary>
        /// 在 LoadingTriggerSensor 模式下，等待上车触发绑定的最长超时时间（单位：毫秒，建议范围：1000~30000；设置为 0 则禁用超时）。
        /// 队头包裹超过此时长仍未收到上车触发时，自动回退到创建时间成熟，避免高密度场景下队头阻塞向后传导放大延迟。
        /// </summary>
        public int LoadingTriggerWaitTimeoutMs { get; set; } = DefaultLoadingTriggerWaitTimeoutMs;
    }
}
