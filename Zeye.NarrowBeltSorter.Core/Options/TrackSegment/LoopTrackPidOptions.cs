namespace Zeye.NarrowBeltSorter.Core.Options.TrackSegment {
    /// <summary>
    /// 环形轨道 PID 参数。
    /// </summary>
    public sealed record LoopTrackPidOptions {
        /// <summary>
        /// 是否启用 PID 闭环稳速。
        /// </summary>
        public bool Enabled { get; init; } = true;

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

        /// <summary>
        /// 输出频率下限（Hz）。
        /// </summary>
        public decimal OutputMinHz { get; init; } = 0m;

        /// <summary>
        /// 输出频率上限（Hz）。
        /// </summary>
        public decimal OutputMaxHz { get; init; } = 25m;

        /// <summary>
        /// 积分累计下限。
        /// </summary>
        public decimal IntegralMin { get; init; } = -10m;

        /// <summary>
        /// 积分累计上限。
        /// </summary>
        public decimal IntegralMax { get; init; } = 10m;

        /// <summary>
        /// 微分滤波系数（范围 0~1）。
        /// </summary>
        public decimal DerivativeFilterAlpha { get; init; } = 0.2m;

        /// <summary>
        /// 非运行状态是否冻结积分。
        /// </summary>
        public bool FreezeIntegralWhenNotRunning { get; init; } = true;
    }
}
