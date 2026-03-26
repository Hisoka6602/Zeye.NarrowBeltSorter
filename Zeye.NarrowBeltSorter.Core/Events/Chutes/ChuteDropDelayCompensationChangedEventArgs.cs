namespace Zeye.NarrowBeltSorter.Core.Events.Chutes {
    /// <summary>
    /// 格口落格延迟补偿变更事件载荷
    /// </summary>
    public readonly record struct ChuteDropDelayCompensationChangedEventArgs {
        /// <summary>
        /// 格口 Id
        /// </summary>
        public required long ChuteId { get; init; }

        /// <summary>
        /// 变更前补偿
        /// </summary>
        public required TimeSpan OldCompensation { get; init; }

        /// <summary>
        /// 变更后补偿
        /// </summary>
        public required TimeSpan NewCompensation { get; init; }

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
