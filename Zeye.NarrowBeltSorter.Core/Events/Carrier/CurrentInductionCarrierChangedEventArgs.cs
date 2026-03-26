namespace Zeye.NarrowBeltSorter.Core.Events.Carrier {
    /// <summary>
    /// 当前感应位小车变更事件载荷
    /// </summary>
    public readonly record struct CurrentInductionCarrierChangedEventArgs {
        /// <summary>
        /// 变更前小车 Id（未知时为 null）
        /// </summary>
        public long? OldCarrierId { get; init; }

        /// <summary>
        /// 变更后小车 Id（未知时为 null）
        /// </summary>
        public long? NewCarrierId { get; init; }

        /// <summary>
        /// 变更时间（本地时间语义）
        /// </summary>
        public required DateTime ChangedAt { get; init; }
    }
}
