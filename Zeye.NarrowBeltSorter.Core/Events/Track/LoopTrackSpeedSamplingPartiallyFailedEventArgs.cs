namespace Zeye.NarrowBeltSorter.Core.Events.Track {
    /// <summary>
    /// 速度采样部分失败事件载荷
    /// </summary>
    public readonly record struct LoopTrackSpeedSamplingPartiallyFailedEventArgs {
        public required int SuccessCount { get; init; }
        public required int FailCount { get; init; }
        public required DateTime OccurredAt { get; init; }
    }
}
