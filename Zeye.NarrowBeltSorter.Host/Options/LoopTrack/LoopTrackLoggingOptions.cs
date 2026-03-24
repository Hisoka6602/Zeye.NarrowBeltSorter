namespace Zeye.NarrowBeltSorter.Host.Options.LoopTrack {
    /// <summary>
    /// 环形轨道日志配置。
    /// </summary>
    public sealed record LoopTrackLoggingOptions {
        /// <summary>
        /// 是否启用详细状态调试日志。
        /// </summary>
        public bool EnableVerboseStatus { get; init; }

        /// <summary>
        /// Info 状态日志输出间隔（毫秒）。
        /// </summary>
        public int InfoStatusIntervalMs { get; init; } = 3000;

        /// <summary>
        /// 调试状态日志输出间隔（毫秒）。
        /// </summary>
        public int DebugStatusIntervalMs { get; init; } = 1000;

        /// <summary>
        /// 失稳偏差阈值（mm/s）。
        /// </summary>
        public decimal UnstableDeviationThresholdMmps { get; init; } = 100m;

        /// <summary>
        /// 失稳持续判定时长（毫秒）。
        /// </summary>
        public int UnstableDurationMs { get; init; } = 3000;
    }
}
