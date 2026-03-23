using Zeye.NarrowBeltSorter.Core.Enums.Track;

namespace Zeye.NarrowBeltSorter.Core.Events.Track {
    /// <summary>
    /// 稳速状态变更事件载荷
    /// </summary>
    public readonly record struct LoopTrackStabilizationStatusChangedEventArgs {
        /// <summary>
        /// 变更前状态
        /// </summary>
        public required LoopTrackStabilizationStatus OldStatus { get; init; }

        /// <summary>
        /// 变更后状态
        /// </summary>
        public required LoopTrackStabilizationStatus NewStatus { get; init; }

        /// <summary>
        /// 稳速耗时（无则为 null）
        /// </summary>
        public TimeSpan? StabilizationElapsed { get; init; }

        /// <summary>
        /// 状态变更时刻的实时速度（mm/s）
        /// </summary>
        public decimal RealTimeSpeedMmps { get; init; }

        /// <summary>
        /// 状态变更时刻的目标速度（mm/s）
        /// </summary>
        public decimal TargetSpeedMmps { get; init; }

        /// <summary>
        /// 变更时间
        /// </summary>
        public required DateTime ChangedAt { get; init; }

        /// <summary>
        /// 备注（可空）
        /// </summary>
        public string? Message { get; init; }
    }
}
