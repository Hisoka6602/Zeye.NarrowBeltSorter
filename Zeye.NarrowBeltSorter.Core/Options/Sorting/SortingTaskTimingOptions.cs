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
        /// 上车匹配补偿激活占比阈值默认值（百分比，0~100）。
        /// </summary>
        public const int DefaultLoadingMatchCompensationEnterPercent = 95;

        /// <summary>
        /// 上车匹配补偿退出占比阈值默认值（百分比，0~100）。
        /// </summary>
        public const int DefaultLoadingMatchCompensationExitPercent = 90;

        /// <summary>
        /// 上车匹配延迟占比平滑窗口大小默认值。
        /// </summary>
        public const int DefaultLoadingMatchSmoothingWindowSize = 1;

        /// <summary>
        /// 环线速度合法范围最小值默认值（mm/s）。
        /// </summary>
        public const decimal DefaultRealtimeSpeedValidMinMmps = 100m;

        /// <summary>
        /// 环线速度合法范围最大值默认值（mm/s）。
        /// </summary>
        public const decimal DefaultRealtimeSpeedValidMaxMmps = 3000m;

        /// <summary>
        /// 包裹从创建到进入待装车队列的成熟延迟（单位：ms，最小值：0，建议范围：500~10000）。
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
        /// 格口开门到关门的间隔时间（单位：ms，最小值：0，建议范围：100~2000）。
        /// </summary>
        public int ChuteOpenCloseIntervalMs { get; set; } = DefaultChuteOpenCloseIntervalMs;

        /// <summary>
        /// 链路阶段耗时告警阈值（单位：毫秒，建议范围：500~30000）。
        /// 任意链路阶段（上车触发→上车成功、上车成功→到达格口）耗时超过此阈值时输出告警日志并附带上下文信息。
        /// </summary>
        public int ParcelChainAlertThresholdMs { get; set; } = DefaultParcelChainAlertThresholdMs;

        /// <summary>
        /// 小车步距（单位：mm，取值范围：大于 0，建议范围：100~2000）。
        /// 用于计算单步距时间 CarrierPeriodMs = CarrierPitchMm / RealtimeSpeedMmps * 1000，
        /// 进而推导延迟占比 DelayRatio = EffectiveDelayMs / CarrierPeriodMs。
        /// 配置值小于等于 0 时补偿计算降级为固定偏移（FallbackReason=InvalidCarrierPitchMm）。
        /// </summary>
        public decimal CarrierPitchMm { get; set; }

        /// <summary>
        /// 是否启用上车匹配时序补偿（取值：true/false，默认关闭以支持灰度；
        /// 仅当 CurrentInductionCarrierChanged.ChangedAt 完成传感器触发时间透传改造后才允许置为 true）。
        /// </summary>
        public bool EnableLoadingMatchTimeCompensation { get; set; }

        /// <summary>
        /// 上车匹配补偿偏移量 delta（首版仅允许填写 0 或 +1；
        /// 正数表示顺时针偏移一车，0 表示不偏移；
        /// 超出首版推荐范围时系统仍会执行 delta 偏移而不降级，请谨慎配置）。
        /// </summary>
        public int LoadingMatchCompensationDelta { get; set; }

        /// <summary>
        /// 上车匹配补偿激活阈值（百分比，取值范围：0~100，须大于 LoadingMatchCompensationExitPercent；默认 95）。
        /// DelayRatio 上穿此阈值时进入补偿态（与 Exit 联合形成滞回门限）。
        /// </summary>
        public int LoadingMatchCompensationEnterPercent { get; set; } = DefaultLoadingMatchCompensationEnterPercent;

        /// <summary>
        /// 上车匹配补偿退出阈值（百分比，取值范围：0~100，须小于 LoadingMatchCompensationEnterPercent；默认 90）。
        /// DelayRatio 下穿此阈值时退出补偿态（与 Enter 联合形成滞回门限）。
        /// </summary>
        public int LoadingMatchCompensationExitPercent { get; set; } = DefaultLoadingMatchCompensationExitPercent;

        /// <summary>
        /// 延迟占比平滑窗口大小（取值范围：1~20，1 表示关闭平滑，直接使用原始占比；默认 1）。
        /// 同时配置滞回与平滑时：先对 DelayRatio 做平滑，再判定 Enter/Exit 门限。
        /// </summary>
        public int LoadingMatchSmoothingWindowSize { get; set; } = DefaultLoadingMatchSmoothingWindowSize;

        /// <summary>
        /// 环线速度合法范围最小值（单位：mm/s，取值范围：大于 0 且小于 RealtimeSpeedValidMaxMmps；默认 100）。
        /// 实时速度低于此值时降级为固定偏移。
        /// </summary>
        public decimal RealtimeSpeedValidMinMmps { get; set; } = DefaultRealtimeSpeedValidMinMmps;

        /// <summary>
        /// 环线速度合法范围最大值（单位：mm/s，取值范围：大于 RealtimeSpeedValidMinMmps；默认 3000）。
        /// 实时速度高于此值时降级为固定偏移。
        /// </summary>
        public decimal RealtimeSpeedValidMaxMmps { get; set; } = DefaultRealtimeSpeedValidMaxMmps;
    }
}
