using Zeye.NarrowBeltSorter.Core.Enums.Track;

namespace Zeye.NarrowBeltSorter.Core.Events.Track {
    /// <summary>
    /// 多从站速度差异较大事件载荷
    /// </summary>
    public readonly record struct LoopTrackSpeedSpreadTooLargeEventArgs {
        /// <summary>
        /// 当前使用的速度汇总策略
        /// </summary>
        public required SpeedAggregateStrategy Strategy { get; init; }

        /// <summary>
        /// 各从站速度极差（mm/s）
        /// </summary>
        public required decimal SpreadMmps { get; init; }

        /// <summary>
        /// 各从站采样值快照（格式：SlaveId=mmps 逗号分隔）
        /// </summary>
        public required string Samples { get; init; }

        /// <summary>
        /// 事件发生时间（本地时间）
        /// </summary>
        public required DateTime OccurredAt { get; init; }
    }
}
