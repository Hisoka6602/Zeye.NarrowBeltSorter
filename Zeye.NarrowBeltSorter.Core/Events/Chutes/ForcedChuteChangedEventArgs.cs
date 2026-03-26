namespace Zeye.NarrowBeltSorter.Core.Events.Chutes {
    /// <summary>
    /// 强排格口变更事件载荷
    /// </summary>
    public readonly record struct ForcedChuteChangedEventArgs {
        /// <summary>
        /// 变更前强排格口 Id（未设置时为 null）
        /// </summary>
        public long? OldForcedChuteId { get; init; }

        /// <summary>
        /// 变更后强排格口 Id（未设置时为 null）
        /// </summary>
        public long? NewForcedChuteId { get; init; }

        /// <summary>
        /// 变更时间（本地时间语义）
        /// </summary>
        public required DateTime ChangedAt { get; init; }
    }
}
