namespace Zeye.NarrowBeltSorter.Core.Events.Carrier {
    /// <summary>
    /// 小车载货状态变更事件载荷
    /// </summary>
    public readonly record struct CarrierLoadStatusChangedEventArgs {
        /// <summary>
        /// 小车 Id
        /// </summary>
        public required long CarrierId { get; init; }

        /// <summary>
        /// 变更前是否载货
        /// </summary>
        public required bool OldIsLoaded { get; init; }

        /// <summary>
        /// 变更后是否载货
        /// </summary>
        public required bool NewIsLoaded { get; init; }

        /// <summary>
        /// 变更前包裹 Id（无包裹时为 null）
        /// </summary>
        public long? OldParcelId { get; init; }

        /// <summary>
        /// 变更后包裹 Id（无包裹时为 null）
        /// </summary>
        public long? NewParcelId { get; init; }

        /// <summary>
        /// 感应区小车 Id（未知时为 null）
        /// </summary>
        public long? CurrentInductionCarrierId { get; init; }

        /// <summary>
        /// 变更时间（本地时间语义）
        /// </summary>
        public required DateTime ChangedAt { get; init; }
    }
}
