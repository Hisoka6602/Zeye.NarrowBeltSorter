using Zeye.NarrowBeltSorter.Core.Enums.Chutes;

namespace Zeye.NarrowBeltSorter.Core.Events.Chutes {
    /// <summary>
    /// 格口状态变更事件载荷
    /// </summary>
    public readonly record struct ChuteStatusChangedEventArgs {
        /// <summary>
        /// 格口 Id
        /// </summary>
        public required long ChuteId { get; init; }

        /// <summary>
        /// 变更前状态
        /// </summary>
        public required ChuteStatus OldStatus { get; init; }

        /// <summary>
        /// 变更后状态
        /// </summary>
        public required ChuteStatus NewStatus { get; init; }

        /// <summary>
        /// 变更时间（本地时间语义）
        /// </summary>
        public required DateTime ChangedAt { get; init; }

        /// <summary>
        /// 变更原因（可空）
        /// </summary>
        public string? Reason { get; init; }
    }
}
