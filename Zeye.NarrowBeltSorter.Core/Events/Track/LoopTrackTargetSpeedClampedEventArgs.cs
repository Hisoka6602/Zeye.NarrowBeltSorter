namespace Zeye.NarrowBeltSorter.Core.Events.Track {
    /// <summary>
    /// 目标速度被限幅事件载荷
    /// </summary>
    public readonly record struct LoopTrackTargetSpeedClampedEventArgs {
        public required string Operation { get; init; }
        public required decimal RequestedMmps { get; init; }
        public required decimal LimitedMmps { get; init; }
        public required decimal ClampMaxHz { get; init; }
        public required decimal MmpsPerHz { get; init; }
        public required DateTime OccurredAt { get; init; }
    }
}
