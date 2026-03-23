namespace Zeye.NarrowBeltSorter.Core.Options.TrackSegment {
    /// <summary>
    /// 环形轨道连接参数。
    /// </summary>
    public sealed record class LoopTrackConnectionOptions {
        /// <summary>
        /// 从站地址。
        /// </summary>
        public byte SlaveAddress { get; init; } = 1;

        /// <summary>
        /// 通讯超时时间（毫秒）。
        /// </summary>
        public int TimeoutMilliseconds { get; init; } = 1000;

        /// <summary>
        /// 重试次数。
        /// </summary>
        public int RetryCount { get; init; } = 2;
    }
}
