namespace Zeye.NarrowBeltSorter.Core.Events.Chutes {
    /// <summary>
    /// 格口包裹落格事件载荷
    /// </summary>
    public readonly record struct ChuteParcelDroppedEventArgs {
        /// <summary>
        /// 格口 Id
        /// </summary>
        public required long ChuteId { get; init; }

        /// <summary>
        /// 包裹 Id
        /// </summary>
        public required long ParcelId { get; init; }

        /// <summary>
        /// 落格时间（本地时间语义）
        /// </summary>
        public required DateTime DroppedAt { get; init; }
    }
}
