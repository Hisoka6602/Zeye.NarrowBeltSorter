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
        /// 调试状态日志输出间隔（毫秒）。
        /// </summary>
        public int DebugStatusIntervalMs { get; init; } = 1000;
    }
}
