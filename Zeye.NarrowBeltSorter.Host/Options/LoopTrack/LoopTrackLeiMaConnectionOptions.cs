namespace Zeye.NarrowBeltSorter.Host.Options.LoopTrack {
    /// <summary>
    /// 雷码连接参数配置。
    /// </summary>
    public sealed record LoopTrackLeiMaConnectionOptions {
        /// <summary>
        /// 远端地址，格式示例：127.0.0.1:502。
        /// </summary>
        public string RemoteHost { get; set; } = "127.0.0.1:502";

        /// <summary>
        /// Modbus 从站地址（1~247）。
        /// </summary>
        public byte SlaveAddress { get; set; } = 1;

        /// <summary>
        /// 通讯超时（毫秒）。
        /// </summary>
        public int TimeoutMs { get; set; } = 1000;

        /// <summary>
        /// 重试次数（不含首次）。
        /// </summary>
        public int RetryCount { get; set; } = 2;

        /// <summary>
        /// 最高输出频率（Hz）。
        /// </summary>
        public decimal MaxOutputHz { get; set; } = 50m;

        /// <summary>
        /// P3.10 转矩给定最大原始值。
        /// </summary>
        public ushort MaxTorqueRawUnit { get; set; } = 1000;
    }
}
