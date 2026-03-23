namespace Zeye.NarrowBeltSorter.Core.Events.Track {
    /// <summary>
    /// 频率给定偏低事件载荷
    /// </summary>
    public readonly record struct LoopTrackLowFrequencySetpointEventArgs {
        public required decimal EstimatedMmps { get; init; }
        public required ushort RawUnit { get; init; }
        public required decimal TargetHz { get; init; }
        public required decimal ThresholdHz { get; init; }
        public required DateTime OccurredAt { get; init; }
    }
}
