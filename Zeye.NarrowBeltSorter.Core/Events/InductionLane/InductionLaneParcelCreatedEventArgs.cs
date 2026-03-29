namespace Zeye.NarrowBeltSorter.Core.Events.InductionLane {
    /// <summary>
    /// 供包台包裹创建事件载荷
    /// </summary>
    public readonly record struct InductionLaneParcelCreatedEventArgs {
        /// <summary>
        /// 供包台 Id
        /// </summary>
        public required long InductionLaneId { get; init; }

        /// <summary>
        /// 包裹 Id
        /// </summary>
        public required long ParcelId { get; init; }

        /// <summary>
        /// 事件时间（本地时间语义）
        /// </summary>
        public required DateTime OccurredAt { get; init; }
    }
}
