namespace Zeye.NarrowBeltSorter.Core.Events.Carrier {
    /// <summary>
    /// 小车靠近目标格口即将分拣事件载荷
    /// </summary>
    public readonly record struct CarrierApproachingTargetChuteEventArgs {
        /// <summary>
        /// 小车 Id
        /// </summary>
        public required long CarrierId { get; init; }

        /// <summary>
        /// 包裹 Id
        /// </summary>
        public required long ParcelId { get; init; }

        /// <summary>
        /// 目标格口 Id
        /// </summary>
        public required long TargetChuteId { get; init; }

        /// <summary>
        /// 感应区小车 Id
        /// </summary>
        public required long CurrentInductionCarrierId { get; init; }

        /// <summary>
        /// 与目标格口对应小车的环形距离（单位：小车数）
        /// </summary>
        public required int DistanceToTarget { get; init; }

        /// <summary>
        /// 事件时间（本地时间语义）
        /// </summary>
        public required DateTime OccurredAt { get; init; }
    }
}
