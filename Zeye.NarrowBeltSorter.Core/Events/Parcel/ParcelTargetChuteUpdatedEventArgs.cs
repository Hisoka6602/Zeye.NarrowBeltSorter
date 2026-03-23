namespace Zeye.NarrowBeltSorter.Core.Events.Parcel {
    /// <summary>
    /// 包裹目标格口更新事件载荷
    /// </summary>
    public readonly record struct ParcelTargetChuteUpdatedEventArgs {
        /// <summary>
        /// 包裹Id
        /// </summary>
        public required long ParcelId { get; init; }

        /// <summary>
        /// 旧目标格口Id
        /// </summary>
        public long? OldTargetChuteId { get; init; }

        /// <summary>
        /// 新目标格口Id
        /// </summary>
        public required long NewTargetChuteId { get; init; }

        /// <summary>
        /// 赋值时间（本地时间语义，DateTimeKind.Local，约定不得写入 UTC 或 Unspecified）
        /// </summary>
        public required DateTime AssignedAt { get; init; }
    }
}
