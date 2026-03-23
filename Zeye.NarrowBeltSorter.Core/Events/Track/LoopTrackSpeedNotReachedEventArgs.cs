namespace Zeye.NarrowBeltSorter.Core.Events.Track {
    /// <summary>
    /// 速度长时间未达到目标事件载荷
    /// </summary>
    public readonly record struct LoopTrackSpeedNotReachedEventArgs {
        public required decimal TargetMmps { get; init; }
        public required decimal ActualMmps { get; init; }
        public required decimal TargetHz { get; init; }
        public required decimal ActualHz { get; init; }
        public required decimal IssuedHz { get; init; }
        public required decimal GapHz { get; init; }
        public required string LimitReason { get; init; }
        public required DateTime OccurredAt { get; init; }
    }
}
