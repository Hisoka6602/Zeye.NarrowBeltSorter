namespace Zeye.NarrowBeltSorter.Core.Events.Parcel {
    /// <summary>
    /// 包裹落格事件载荷
    /// </summary>
    public readonly record struct ParcelDroppedEventArgs {
        /// <summary>
        /// 包裹Id
        /// </summary>
        public required long ParcelId { get; init; }

        /// <summary>
        /// 实际落格格口Id
        /// </summary>
        public required long ActualChuteId { get; init; }

        /// <summary>
        /// 落格时间（本地时间语义，DateTimeKind.Local，约定不得写入 UTC 或 Unspecified）
        /// </summary>
        public required DateTime DroppedAt { get; init; }
    }
}
