namespace Zeye.NarrowBeltSorter.Core.Events.Parcel {
    /// <summary>
    /// 包裹创建事件载荷
    /// </summary>
    public readonly record struct ParcelCreatedEventArgs {
        /// <summary>
        /// 包裹Id
        /// </summary>
        public required long ParcelId { get; init; }
        /// <summary>
        /// 包裹信息快照
        /// </summary>
        public required ParcelInfo Parcel { get; init; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public required DateTimeOffset CreatedAt { get; init; }
    }
}
