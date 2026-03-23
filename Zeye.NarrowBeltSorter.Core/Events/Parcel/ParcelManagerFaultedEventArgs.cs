namespace Zeye.NarrowBeltSorter.Core.Events.Parcel {
    /// <summary>
    /// 包裹管理器异常事件载荷（用于隔离异常，不影响上层调用链）
    /// </summary>
    public readonly record struct ParcelManagerFaultedEventArgs {
        /// <summary>
        /// 异常描述
        /// </summary>
        public required string Message { get; init; }

        /// <summary>
        /// 异常对象
        /// </summary>
        public required Exception Exception { get; init; }

        /// <summary>
        /// 发生时间（本地时间语义，DateTimeKind.Local，约定不得写入 UTC 或 Unspecified）
        /// </summary>
        public required DateTime OccurredAt { get; init; }
    }
}
