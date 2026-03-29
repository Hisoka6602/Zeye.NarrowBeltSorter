namespace Zeye.NarrowBeltSorter.Core.Events.InductionLane {
    /// <summary>
    /// 供包台包裹到达上车位事件载荷
    /// </summary>
    public readonly record struct InductionLaneParcelArrivedAtLoadingPositionEventArgs {
        /// <summary>
        /// 供包台 Id
        /// </summary>
        public required long InductionLaneId { get; init; }

        /// <summary>
        /// 包裹 Id
        /// </summary>
        public required long ParcelId { get; init; }

        /// <summary>
        /// 到达时间（本地时间语义）
        /// </summary>
        public required DateTime ArrivedAt { get; init; }
    }
}
