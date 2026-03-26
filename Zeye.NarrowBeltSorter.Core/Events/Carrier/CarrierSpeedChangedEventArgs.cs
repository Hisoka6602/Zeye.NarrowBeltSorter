namespace Zeye.NarrowBeltSorter.Core.Events.Carrier {
    /// <summary>
    /// 小车速度变更事件载荷
    /// </summary>
    public readonly record struct CarrierSpeedChangedEventArgs {
        /// <summary>
        /// 小车 Id
        /// </summary>
        public required long CarrierId { get; init; }

        /// <summary>
        /// 变更前速度（mm/s）
        /// </summary>
        public required decimal OldSpeed { get; init; }

        /// <summary>
        /// 变更后速度（mm/s）
        /// </summary>
        public required decimal NewSpeed { get; init; }

        /// <summary>
        /// 变更时间（本地时间语义）
        /// </summary>
        public required DateTime ChangedAt { get; init; }
    }
}
