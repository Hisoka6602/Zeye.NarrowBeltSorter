namespace Zeye.NarrowBeltSorter.Core.Events.Track {
    /// <summary>
    /// 速度变更事件载荷
    /// </summary>
    public readonly record struct LoopTrackSpeedChangedEventArgs {
        /// <summary>
        /// 变更前实时速度（mm/s）
        /// </summary>
        public required decimal OldRealTimeSpeedMmps { get; init; }

        /// <summary>
        /// 变更后实时速度（mm/s）
        /// </summary>
        public required decimal NewRealTimeSpeedMmps { get; init; }

        /// <summary>
        /// 当前目标速度（mm/s）
        /// </summary>
        public required decimal TargetSpeedMmps { get; init; }

        /// <summary>
        /// 变更时间
        /// </summary>
        public required DateTime ChangedAt { get; init; }
    }
}
