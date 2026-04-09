namespace Zeye.NarrowBeltSorter.Core.Options.LoopTrack {
    /// <summary>
    /// 环形轨道连接重试配置。
    /// </summary>
    public sealed record LoopTrackConnectRetryOptions {
        /// <summary>
        /// 最大重试次数（不含首次连接，最小值：0，建议范围：1~10）。
        /// </summary>
        public int MaxAttempts { get; init; } = 3;

        /// <summary>
        /// 初始重试间隔（单位：ms，最小值：100，建议范围：500~5000）。
        /// </summary>
        public int DelayMs { get; init; } = 1000;

        /// <summary>
        /// 重试间隔上限（单位：ms，必须大于等于 DelayMs，建议范围：1000~30000）。
        /// </summary>
        public int MaxDelayMs { get; init; } = 5000;
    }
}
