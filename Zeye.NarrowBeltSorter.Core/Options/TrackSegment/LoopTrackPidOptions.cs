namespace Zeye.NarrowBeltSorter.Core.Options.TrackSegment {
    /// <summary>
    /// 环形轨道 PID 参数。
    /// </summary>
    public sealed record class LoopTrackPidOptions {
        /// <summary>
        /// 比例系数。
        /// </summary>
        public decimal Kp { get; init; } = 1m;

        /// <summary>
        /// 积分系数。
        /// </summary>
        public decimal Ki { get; init; } = 0m;

        /// <summary>
        /// 微分系数。
        /// </summary>
        public decimal Kd { get; init; } = 0m;
    }
}
