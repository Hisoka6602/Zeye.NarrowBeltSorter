namespace Zeye.NarrowBeltSorter.Core.Options.TrackSegment {
    /// <summary>
    /// 环形轨道连接参数。
    /// </summary>
    public sealed record LoopTrackConnectionOptions {
        /// <summary>
        /// 从站地址（范围：1~247）。
        /// </summary>
        public byte SlaveAddress { get; init; } = 1;

        /// <summary>
        /// 通讯超时时间（单位：ms，最小值：100，建议范围：500~5000）。
        /// </summary>
        public int TimeoutMilliseconds { get; init; } = 1000;

        /// <summary>
        /// 重试次数（不含首次，最小值：0，建议范围：1~5）。
        /// </summary>
        public int RetryCount { get; init; } = 2;
    }
}
