using Zeye.NarrowBeltSorter.Core.Enums.Track;

namespace Zeye.NarrowBeltSorter.Core.Events.Track {
    /// <summary>
    /// 运行状态变更事件载荷
    /// </summary>
    public readonly record struct LoopTrackRunStatusChangedEventArgs {
        /// <summary>
        /// 变更前状态
        /// </summary>
        public required LoopTrackRunStatus OldStatus { get; init; }

        /// <summary>
        /// 变更后状态
        /// </summary>
        public required LoopTrackRunStatus NewStatus { get; init; }

        /// <summary>
        /// 变更时间
        /// </summary>
        public required DateTime ChangedAt { get; init; }

        /// <summary>
        /// 备注（可空）
        /// </summary>
        public string? Message { get; init; }
    }
}
