using Zeye.NarrowBeltSorter.Core.Enums.Emc;

namespace Zeye.NarrowBeltSorter.Core.Events.Emc {
    /// <summary>
    /// EMC 状态变化事件载荷。
    /// </summary>
    public readonly record struct EmcStatusChangedEventArgs {
        /// <summary>
        /// 变更前状态。
        /// </summary>
        public required EmcControllerStatus OldStatus { get; init; }

        /// <summary>
        /// 变更后状态。
        /// </summary>
        public required EmcControllerStatus NewStatus { get; init; }

        /// <summary>
        /// 状态变化时间（本地时间语义）。
        /// </summary>
        public required DateTime ChangedAt { get; init; }

        /// <summary>
        /// 状态变化原因。
        /// </summary>
        public string? Reason { get; init; }
    }
}
