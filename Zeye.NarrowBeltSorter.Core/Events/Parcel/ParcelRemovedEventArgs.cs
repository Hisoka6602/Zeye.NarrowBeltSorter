namespace Zeye.NarrowBeltSorter.Core.Events.Parcel {
    /// <summary>
    /// 包裹移除事件载荷
    /// </summary>
    public readonly record struct ParcelRemovedEventArgs {
        /// <summary>
        /// 包裹Id
        /// </summary>
        public required long ParcelId { get; init; }

        /// <summary>
        /// 移除原因（可选：超时、完成、异常口、手工清理等）
        /// </summary>
        public string? Reason { get; init; }

        /// <summary>
        /// 移除时间
        /// </summary>
        public required DateTime RemovedAt { get; init; }
    }
}
