namespace Zeye.NarrowBeltSorter.Core.Events.Chutes {
    /// <summary>
    /// 格口锁格状态变更事件载荷
    /// </summary>
    public readonly record struct ChuteLockStatusChangedEventArgs {
        /// <summary>
        /// 格口 Id
        /// </summary>
        public required long ChuteId { get; init; }

        /// <summary>
        /// 变更前是否锁格
        /// </summary>
        public required bool OldIsLocked { get; init; }

        /// <summary>
        /// 变更后是否锁格
        /// </summary>
        public required bool NewIsLocked { get; init; }

        /// <summary>
        /// 变更时间（本地时间语义）
        /// </summary>
        public required DateTime ChangedAt { get; init; }
    }
}
