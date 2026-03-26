namespace Zeye.NarrowBeltSorter.Core.Events.Carrier {
    /// <summary>
    /// 小车建环完成事件载荷
    /// </summary>
    public readonly record struct CarrierRingBuiltEventArgs {
        /// <summary>
        /// 是否建环成功
        /// </summary>
        public required bool IsBuilt { get; init; }

        /// <summary>
        /// 建环时间（本地时间语义）
        /// </summary>
        public required DateTime BuiltAt { get; init; }

        /// <summary>
        /// 备注（可空）
        /// </summary>
        public string? Message { get; init; }
    }
}
