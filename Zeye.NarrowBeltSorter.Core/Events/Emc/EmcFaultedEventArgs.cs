namespace Zeye.NarrowBeltSorter.Core.Events.Emc {
    /// <summary>
    /// EMC 故障事件载荷。
    /// </summary>
    public readonly record struct EmcFaultedEventArgs {
        /// <summary>
        /// 故障代码。
        /// </summary>
        public required int FaultCode { get; init; }

        /// <summary>
        /// 故障消息。
        /// </summary>
        public required string Message { get; init; }

        /// <summary>
        /// 故障时间（本地时间语义）。
        /// </summary>
        public required DateTime FaultedAt { get; init; }

        /// <summary>
        /// 异常对象（可空）。
        /// </summary>
        public Exception? Exception { get; init; }
    }
}
