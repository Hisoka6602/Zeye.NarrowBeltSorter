using Zeye.NarrowBeltSorter.Core.Utilities.LoopTrack;

namespace Zeye.NarrowBeltSorter.Core.Options.LoopTrack {
    /// <summary>
    /// 雷码连接参数配置。
    /// </summary>
    public sealed record LoopTrackLeiMaConnectionOptions {
        /// <summary>
        /// 传输模式（TcpGateway/SerialRtu）。
        /// </summary>
        public string Transport { get; set; } = LoopTrackLeiMaTransportModes.TcpGateway;

        /// <summary>
        /// 远端地址，格式示例：127.0.0.1:502。
        /// </summary>
        public string RemoteHost { get; set; } = "127.0.0.1:502";

        /// <summary>
        /// 串口 RTU 参数配置。
        /// </summary>
        public LoopTrackLeiMaSerialRtuOptions SerialRtu { get; set; } = new();

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
        /// 最高输出频率（Hz，参考 ZakYip 配置默认值 25）。
        /// </summary>
        public decimal MaxOutputHz { get; set; } = 25m;

        /// <summary>
        /// P3.10 转矩给定最大原始值。
        /// </summary>
        public ushort MaxTorqueRawUnit { get; set; } = 1000;

        /// <summary>
        /// P3.10 写入最小间隔（毫秒）。
        /// </summary>
        public int TorqueSetpointWriteIntervalMs { get; set; } = 300;
    }
}
