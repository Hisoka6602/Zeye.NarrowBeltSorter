namespace Zeye.NarrowBeltSorter.Core.Events.Track {
    /// <summary>
    /// 频率给定被保护上限限幅事件载荷
    /// </summary>
    public readonly record struct LoopTrackFrequencySetpointHardClampedEventArgs {
        public required ushort RequestedRawUnit { get; init; }
        public required decimal RequestedHz { get; init; }
        public required decimal ClampMaxHz { get; init; }
        public required ushort ClampedRawUnit { get; init; }
        public required DateTime OccurredAt { get; init; }
    }
}
