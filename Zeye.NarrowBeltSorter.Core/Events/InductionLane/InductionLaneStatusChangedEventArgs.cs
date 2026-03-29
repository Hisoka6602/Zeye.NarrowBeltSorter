using Zeye.NarrowBeltSorter.Core.Enums.InductionLane;

namespace Zeye.NarrowBeltSorter.Core.Events.InductionLane {
    /// <summary>
    /// 供包台状态变化事件载荷
    /// </summary>
    public readonly record struct InductionLaneStatusChangedEventArgs {
        /// <summary>
        /// 供包台 Id
        /// </summary>
        public required long InductionLaneId { get; init; }

        /// <summary>
        /// 变更前状态
        /// </summary>
        public required InductionLaneStatus OldStatus { get; init; }

        /// <summary>
        /// 变更后状态
        /// </summary>
        public required InductionLaneStatus NewStatus { get; init; }

        /// <summary>
        /// 变更时间（本地时间语义）
        /// </summary>
        public required DateTime ChangedAt { get; init; }
    }
}
