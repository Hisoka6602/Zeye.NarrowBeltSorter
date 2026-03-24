using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;

namespace Zeye.NarrowBeltSorter.Host.Options.LoopTrack {
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
        /// 状态日志轮询周期（毫秒）。
        /// </summary>
        public int PollingIntervalMs { get; set; } = 300;

        /// <summary>
        /// 雷码连接配置。
        /// </summary>
        public LoopTrackLeiMaConnectionOptions LeiMaConnection { get; set; } = new();

        /// <summary>
        /// PID 参数配置。
        /// </summary>
        public LoopTrackPidOptions Pid { get; set; } = new();
    }
}
