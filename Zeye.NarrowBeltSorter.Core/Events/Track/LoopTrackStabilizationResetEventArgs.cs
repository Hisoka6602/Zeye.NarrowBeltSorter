namespace Zeye.NarrowBeltSorter.Core.Events.Track {
    /// <summary>
    /// 稳速状态重置事件载荷
    /// </summary>
    public readonly record struct LoopTrackStabilizationResetEventArgs {
        /// <summary>
        /// 重置原因
        /// </summary>
        public string? Reason { get; init; }

        /// <summary>
        /// 发生时间
        /// </summary>
        public required DateTime OccurredAt { get; init; }
    }
}
