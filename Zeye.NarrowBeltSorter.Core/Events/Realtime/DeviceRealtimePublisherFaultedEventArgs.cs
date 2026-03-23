namespace Zeye.NarrowBeltSorter.Core.Events.Realtime {
    /// <summary>
    /// 设备实时发布器异常事件载荷
    /// </summary>
    public readonly record struct DeviceRealtimePublisherFaultedEventArgs {
        /// <summary>
        /// 操作名
        /// </summary>
        public required string Operation { get; init; }

        /// <summary>
        /// 异常
        /// </summary>
        public required Exception Exception { get; init; }

        /// <summary>
        /// 发生时间
        /// </summary>
        public required DateTimeOffset FaultedAt { get; init; }
    }
}
