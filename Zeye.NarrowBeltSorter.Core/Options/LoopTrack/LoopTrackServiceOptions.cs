using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;

namespace Zeye.NarrowBeltSorter.Core.Options.LoopTrack {
    /// <summary>
    /// 环形轨道后台服务配置。
    /// </summary>
    public sealed record LoopTrackServiceOptions {
        /// <summary>
        /// 是否启用环轨后台服务（取值：true/false）。
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 轨道名称（用于日志标识，不能为空）。
        /// </summary>
        public string TrackName { get; set; } = "LeiMa-LoopTrack";

        /// <summary>
        /// 是否在连接成功后自动启动轨道（取值：true/false）。
        /// </summary>
        public bool AutoStart { get; set; } = false;

        /// <summary>
        /// 自动启动后设置的目标速度（单位：mm/s，最小值：0，建议范围：0~2500）。
        /// </summary>
        public decimal TargetSpeedMmps { get; set; } = 0m;

        /// <summary>
        /// 环轨管理器轮询周期（单位：ms，最小值：50，建议范围：100~1000）。
        /// </summary>
        public int PollingIntervalMs { get; set; } = 300;

        /// <summary>
        /// 稳速判定容差（单位：mm/s，最小值：0，建议范围：10~200）。
        /// </summary>
        public decimal StabilizedToleranceMmps { get; set; } = 50m;

        /// <summary>
        /// 稳速判定窗口（单位：ms，最小值：100，建议范围：500~5000）。
        /// </summary>
        public int StabilizedWindowMs { get; set; } = 1500;
        /// <summary>
        /// 雷码连接配置。
        /// </summary>
        public LoopTrackLeiMaConnectionOptions LeiMaConnection { get; set; } = new();

        /// <summary>
        /// PID 参数配置。
        /// </summary>
        public LoopTrackPidOptions Pid { get; set; } = new();

        /// <summary>
        /// 连接重试配置。
        /// </summary>
        public LoopTrackConnectRetryOptions ConnectRetry { get; set; } = new();

        /// <summary>
        /// 日志配置。
        /// </summary>
        public LoopTrackLoggingOptions Logging { get; set; } = new();

        /// <summary>
        /// 上机联调（HIL）后台配置。
        /// </summary>
        public LoopTrackHilOptions Hil { get; set; } = new();

        /// <summary>
        /// 检修状态下的目标速度（mm/s）。
        /// 当检修开关传感器打开时，轨道以此速度运行；建议范围：0~2500。
        /// </summary>
        public decimal MaintenanceSpeedMmps { get; set; } = 500m;
    }
}
