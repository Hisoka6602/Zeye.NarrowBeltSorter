namespace Zeye.NarrowBeltSorter.Core.Events.Emc {
    /// <summary>
    /// EMC 初始化完成事件载荷。
    /// </summary>
    public readonly record struct EmcInitializedEventArgs {
        /// <summary>
        /// 初始化完成时间（本地时间语义）。
        /// </summary>
        public required DateTime InitializedAt { get; init; }
    }
}
