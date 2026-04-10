namespace Zeye.NarrowBeltSorter.Core.Events.Track {
    /// <summary>
    /// 频率给定偏低事件载荷
    /// </summary>
    public readonly record struct LoopTrackLowFrequencySetpointEventArgs {
        /// <summary>
        /// 估算目标速度（mm/s）
        /// </summary>
        public required decimal EstimatedMmps { get; init; }

        /// <summary>
        /// 计算出的 P3.10 raw 单位值
        /// </summary>
        public required ushort RawUnit { get; init; }

        /// <summary>
        /// 目标频率（Hz）
        /// </summary>
        public required decimal TargetHz { get; init; }

        /// <summary>
        /// 低频判定阈值（Hz）
        /// </summary>
        public required decimal ThresholdHz { get; init; }

        /// <summary>
        /// 事件发生时间（本地时间）
        /// </summary>
        public required DateTime OccurredAt { get; init; }
    }
}
