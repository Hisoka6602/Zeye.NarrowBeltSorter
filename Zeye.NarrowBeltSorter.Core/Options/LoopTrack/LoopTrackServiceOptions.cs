using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;

namespace Zeye.NarrowBeltSorter.Core.Options.LoopTrack {
    /// <summary>
    /// 环形轨道后台服务配置。
    /// </summary>
    public sealed record LoopTrackServiceOptions {
        /// <summary>
        /// 是否启用环轨后台服务。
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 轨道名称。
        /// </summary>
        public string TrackName { get; set; } = "LeiMa-LoopTrack";

        /// <summary>
        /// 是否在连接成功后自动启动轨道。
        /// </summary>
        public bool AutoStart { get; set; } = false;

        /// <summary>
        /// 自动启动后设置的目标速度（mm/s）。
        /// </summary>
        public decimal TargetSpeedMmps { get; set; } = 0m;

        /// <summary>
        /// 环轨管理器轮询周期及状态日志输出间隔（毫秒）。
        /// </summary>
        public int PollingIntervalMs { get; set; } = 300;
        /// <summary>
        /// 稳速判定容差（mm/s）。
        /// </summary>
        public decimal StabilizedToleranceMmps { get; set; } = 50m;

        /// <summary>
        /// 稳速判定窗口（毫秒）。
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
    }
}
