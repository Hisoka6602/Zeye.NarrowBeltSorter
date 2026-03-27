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
        /// 比例系数（现场稳健起步参数）。
        /// </summary>
        public decimal Kp { get; init; } = 0.28m;

        /// <summary>
        /// 积分系数（现场稳健起步参数）。
        /// </summary>
        public decimal Ki { get; init; } = 0.028m;

        /// <summary>
        /// 微分系数（现场稳健起步参数）。
        /// </summary>
        public decimal Kd { get; init; } = 0.005m;

        /// <summary>
        /// 输出控制量下限（P3.10 raw，推荐 0）。
        /// </summary>
        public decimal OutputMinRaw { get; init; } = 0m;

        /// <summary>
        /// 输出控制量上限（P3.10 raw，推荐 1000）。
        /// </summary>
        public decimal OutputMaxRaw { get; init; } = 1000m;

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

        /// <summary>
        /// 轴间同步增益（控制量域单位 / mm·s⁻¹），0 表示禁用同步修正项。
        /// 正值时快轴减少转矩、慢轴增加转矩，从而使各从站速度向平均值收敛；
        /// 当从站速度离散超出稳速容差时，有效增益翻倍以加速轴间同步。
        /// 典型取值范围：0（禁用）~ 2（较强同步）。
        /// </summary>
        public decimal KSync { get; init; } = 0m;
    }
}
