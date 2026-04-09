namespace Zeye.NarrowBeltSorter.Core.Events.Track {
    /// <summary>
    /// 速度长时间未达到目标事件载荷
    /// </summary>
    public readonly record struct LoopTrackSpeedNotReachedEventArgs {
        /// <summary>
        /// 目标速度（mm/s）
        /// </summary>
        public required decimal TargetMmps { get; init; }

        /// <summary>
        /// 实际采样速度（mm/s）
        /// </summary>
        public required decimal ActualMmps { get; init; }

        /// <summary>
        /// 目标频率（Hz）
        /// </summary>
        public required decimal TargetHz { get; init; }

        /// <summary>
        /// 实际采样频率（Hz）
        /// </summary>
        public required decimal ActualHz { get; init; }

        /// <summary>
        /// 最近一次下发频率（Hz）
        /// </summary>
        public required decimal IssuedHz { get; init; }

        /// <summary>
        /// 频率差值（Hz）
        /// </summary>
        public required decimal GapHz { get; init; }

        /// <summary>
        /// 限幅原因描述
        /// </summary>
        public required string LimitReason { get; init; }

        /// <summary>
        /// 事件发生时间（本地时间）
        /// </summary>
        public required DateTime OccurredAt { get; init; }
    }
}
