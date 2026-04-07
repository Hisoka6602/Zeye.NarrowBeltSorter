namespace Zeye.NarrowBeltSorter.Core.Events.Carrier {
    /// <summary>
    /// 小车经过强排格口事件载荷
    /// </summary>
    public readonly record struct CarrierPassedForcedChuteEventArgs {
        /// <summary>
        /// 小车 Id
        /// </summary>
        public required long CarrierId { get; init; }

        /// <summary>
        /// 包裹 Id
        /// </summary>
        public required long ParcelId { get; init; }

        /// <summary>
        /// 强排格口 Id
        /// </summary>
        public required long ForcedChuteId { get; init; }

        /// <summary>
        /// 感应区小车 Id
        /// </summary>
        public required long CurrentInductionCarrierId { get; init; }

        /// <summary>
        /// 事件时间（本地时间语义）
        /// </summary>
        public required DateTime OccurredAt { get; init; }
    }
}
