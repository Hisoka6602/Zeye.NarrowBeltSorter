using Zeye.NarrowBeltSorter.Core.Enums.Chutes;

namespace Zeye.NarrowBeltSorter.Core.Events.Chutes {
    /// <summary>
    /// 格口距离补偿映射变更事件载荷
    /// </summary>
    public readonly record struct ChuteDistanceCompensationChangedEventArgs {
        /// <summary>
        /// 格口 Id
        /// </summary>
        public required long ChuteId { get; init; }

        /// <summary>
        /// 变更前补偿映射
        /// </summary>
        public required IReadOnlyDictionary<ParcelToChuteDistanceLevel, TimeSpan> OldCompensationMap { get; init; }

        /// <summary>
        /// 变更后补偿映射
        /// </summary>
        public required IReadOnlyDictionary<ParcelToChuteDistanceLevel, TimeSpan> NewCompensationMap { get; init; }

        /// <summary>
        /// 变更时间（本地时间语义）
        /// </summary>
        public required DateTime ChangedAt { get; init; }

        /// <summary>
        /// 变更原因（可空）
        /// </summary>
        public string? Reason { get; init; }
    }
}
