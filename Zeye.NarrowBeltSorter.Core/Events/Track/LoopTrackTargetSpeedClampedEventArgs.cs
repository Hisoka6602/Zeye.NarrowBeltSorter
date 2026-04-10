namespace Zeye.NarrowBeltSorter.Core.Events.Track {
    /// <summary>
    /// 目标速度被限幅事件载荷
    /// </summary>
    public readonly record struct LoopTrackTargetSpeedClampedEventArgs {
        /// <summary>
        /// 触发限幅的操作名称
        /// </summary>
        public required string Operation { get; init; }

        /// <summary>
        /// 原始请求目标速度（mm/s）
        /// </summary>
        public required decimal RequestedMmps { get; init; }

        /// <summary>
        /// 限幅后实际生效速度（mm/s）
        /// </summary>
        public required decimal LimitedMmps { get; init; }

        /// <summary>
        /// 频率上限（Hz），对应本次限幅依据
        /// </summary>
        public required decimal ClampMaxHz { get; init; }

        /// <summary>
        /// mm/s 与 Hz 的换算系数（mm/s per Hz）
        /// </summary>
        public required decimal MmpsPerHz { get; init; }

        /// <summary>
        /// 事件发生时间（本地时间）
        /// </summary>
        public required DateTime OccurredAt { get; init; }
    }
}
