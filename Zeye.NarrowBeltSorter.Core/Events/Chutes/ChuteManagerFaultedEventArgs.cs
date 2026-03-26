namespace Zeye.NarrowBeltSorter.Core.Events.Chutes {
    /// <summary>
    /// 格口管理器异常事件载荷（用于隔离异常，不影响上层调用链）
    /// </summary>
    public readonly record struct ChuteManagerFaultedEventArgs {
        /// <summary>
        /// 操作名
        /// </summary>
        public required string Operation { get; init; }

        /// <summary>
        /// 异常对象
        /// </summary>
        public required Exception Exception { get; init; }

        /// <summary>
        /// 发生时间（本地时间语义）
        /// </summary>
        public required DateTime FaultedAt { get; init; }
    }
}
