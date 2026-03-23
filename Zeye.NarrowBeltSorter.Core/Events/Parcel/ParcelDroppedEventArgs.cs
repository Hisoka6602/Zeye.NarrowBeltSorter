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
        /// 落格时间
        /// </summary>
        public required DateTime DroppedAt { get; init; }
    }
}
