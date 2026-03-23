namespace Zeye.NarrowBeltSorter.Core.Events.Track {
    /// <summary>
    /// 管理器异常事件载荷
    /// </summary>
    public readonly record struct LoopTrackManagerFaultedEventArgs {
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
        public required DateTime FaultedAt { get; init; }
    }
}
