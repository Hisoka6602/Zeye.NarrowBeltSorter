namespace Zeye.NarrowBeltSorter.Core.Events.Track {
    /// <summary>
    /// 速度采样部分失败事件载荷
    /// </summary>
    public readonly record struct LoopTrackSpeedSamplingPartiallyFailedEventArgs {
        /// <summary>
        /// 本次采样成功的从站数量
        /// </summary>
        public required int SuccessCount { get; init; }

        /// <summary>
        /// 本次采样失败的从站数量
        /// </summary>
        public required int FailCount { get; init; }

        /// <summary>
        /// 采样失败的从站地址列表（格式：逗号分隔）
        /// </summary>
        public required string FailedSlaveIds { get; init; }

        /// <summary>
        /// 事件发生时间（本地时间）
        /// </summary>
        public required DateTime OccurredAt { get; init; }
    }
}
