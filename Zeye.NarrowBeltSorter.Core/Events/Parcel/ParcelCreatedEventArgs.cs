using Zeye.NarrowBeltSorter.Core.Models.Parcel;

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
        /// 创建时间（本地时间语义，DateTimeKind.Local，约定不得写入 UTC 或 Unspecified）
        /// </summary>
        public required DateTime CreatedAt { get; init; }
    }
}
