using Zeye.NarrowBeltSorter.Core.Enums.Parcel;

namespace Zeye.NarrowBeltSorter.Core.Events.Parcel {
    /// <summary>
    /// 包裹小车集合更新事件载荷
    /// </summary>
    public readonly record struct ParcelCarriersUpdatedEventArgs {
        /// <summary>
        /// 包裹Id
        /// </summary>
        public required long ParcelId { get; init; }

        /// <summary>
        /// 变更类型
        /// </summary>
        public required ParcelCarriersChangeType ChangeType { get; init; }

        /// <summary>
        /// 触发变更的小车Id（无则为 null）
        /// </summary>
        public long? CarrierId { get; init; }

        /// <summary>
        /// 变更时间（本地时间语义，DateTimeKind.Local，约定不得写入 UTC 或 Unspecified）
        /// </summary>
        public required DateTime UpdatedAt { get; init; }

        /// <summary>
        /// 变更后的小车Id集合快照
        /// </summary>
        public required IReadOnlyList<long> CarrierIdsSnapshot { get; init; }
    }
}
