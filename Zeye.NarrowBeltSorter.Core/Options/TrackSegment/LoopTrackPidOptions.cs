namespace Zeye.NarrowBeltSorter.Core.Options.TrackSegment {
    /// <summary>
    /// 环形轨道 PID 参数。
    /// </summary>
    public sealed record LoopTrackPidOptions {
        /// <summary>
        /// 是否启用 PID 闭环稳速（取值：true/false）。
        /// </summary>
        public bool Enabled { get; init; } = true;

        /// <summary>
        /// 比例系数（建议范围：0~10，现场稳健起步参数）。
        /// </summary>
        public decimal Kp { get; init; } = 0.28m;

        /// <summary>
        /// 积分系数（建议范围：0~1，现场稳健起步参数）。
        /// </summary>
        public decimal Ki { get; init; } = 0.028m;

        /// <summary>
        /// 微分系数（建议范围：0~0.1，现场稳健起步参数）。
        /// </summary>
        public decimal Kd { get; init; } = 0.005m;

        /// <summary>
        /// 输出控制量下限（P3.10 raw，推荐：0，必须小于等于 OutputMaxRaw）。
        /// </summary>
        public decimal OutputMinRaw { get; init; } = 0m;

        /// <summary>
        /// 输出控制量上限（P3.10 raw，推荐：1000，必须大于等于 OutputMinRaw）。
        /// </summary>
        public decimal OutputMaxRaw { get; init; } = 1000m;

        /// <summary>
        /// 积分累计下限（必须小于等于 IntegralMax）。
        /// </summary>
        public decimal IntegralMin { get; init; } = -10m;

        /// <summary>
        /// 积分累计上限（必须大于等于 IntegralMin）。
        /// </summary>
        public decimal IntegralMax { get; init; } = 10m;

        /// <summary>
        /// 微分滤波系数（范围：0~1，0 表示不滤波，建议值：0.2）。
        /// </summary>
        public decimal DerivativeFilterAlpha { get; init; } = 0.2m;

        /// <summary>
        /// 非运行状态是否冻结积分（取值：true/false；true 可避免积分饱和）。
        /// </summary>
        public bool FreezeIntegralWhenNotRunning { get; init; } = true;
    }
}
