namespace Zeye.NarrowBeltSorter.Core.Options.LoopTrack {
    /// <summary>
    /// 环形轨道连接重试配置。
    /// </summary>
    public sealed record LoopTrackConnectRetryOptions {
        /// <summary>
        /// 最大重试次数（不含首次连接）。
        /// </summary>
        public int MaxAttempts { get; init; } = 3;

        /// <summary>
        /// 初始重试间隔（毫秒）。
        /// </summary>
        public int DelayMs { get; init; } = 1000;

        /// <summary>
        /// 重试间隔上限（毫秒）。
        /// </summary>
        public int MaxDelayMs { get; init; } = 5000;
    }
}
