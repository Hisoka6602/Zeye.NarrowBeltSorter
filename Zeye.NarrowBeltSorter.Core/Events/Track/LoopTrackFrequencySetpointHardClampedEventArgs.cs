namespace Zeye.NarrowBeltSorter.Core.Events.Track {
    /// <summary>
    /// 频率给定被保护上限限幅事件载荷
    /// </summary>
    public readonly record struct LoopTrackFrequencySetpointHardClampedEventArgs {
        /// <summary>
        /// 原始请求频率（P3.10 raw 单位）
        /// </summary>
        public required ushort RequestedRawUnit { get; init; }

        /// <summary>
        /// 原始请求频率（Hz）
        /// </summary>
        public required decimal RequestedHz { get; init; }

        /// <summary>
        /// 保护上限频率（Hz）
        /// </summary>
        public required decimal ClampMaxHz { get; init; }

        /// <summary>
        /// 限幅后实际写入频率（P3.10 raw 单位）
        /// </summary>
        public required ushort ClampedRawUnit { get; init; }

        /// <summary>
        /// 事件发生时间（本地时间）
        /// </summary>
        public required DateTime OccurredAt { get; init; }
    }
}
