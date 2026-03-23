using Zeye.NarrowBeltSorter.Core.Enums.Track;

namespace Zeye.NarrowBeltSorter.Core.Events.Track {
    /// <summary>
    /// 多从站速度差异较大事件载荷
    /// </summary>
    public readonly record struct LoopTrackSpeedSpreadTooLargeEventArgs {
        public required SpeedAggregateStrategy Strategy { get; init; }
        public required decimal SpreadMmps { get; init; }
        public required string Samples { get; init; }
        public required DateTime OccurredAt { get; init; }
    }
}
