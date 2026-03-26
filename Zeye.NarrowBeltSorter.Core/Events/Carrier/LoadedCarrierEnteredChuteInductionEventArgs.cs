namespace Zeye.NarrowBeltSorter.Core.Events.Carrier {
    /// <summary>
    /// 载货小车进入格口感应区事件载荷
    /// </summary>
    public readonly record struct LoadedCarrierEnteredChuteInductionEventArgs {
        /// <summary>
        /// 小车 Id
        /// </summary>
        public required long CarrierId { get; init; }

        /// <summary>
        /// 格口 Id
        /// </summary>
        public required long ChuteId { get; init; }

        /// <summary>
        /// 包裹 Id（无包裹时为 null）
        /// </summary>
        public long? ParcelId { get; init; }

        /// <summary>
        /// 进入时间（本地时间语义）
        /// </summary>
        public required DateTime EnteredAt { get; init; }
    }
}
