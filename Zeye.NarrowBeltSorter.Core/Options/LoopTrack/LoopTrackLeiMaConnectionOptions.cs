using Zeye.NarrowBeltSorter.Core.Enums.Track;
using Zeye.NarrowBeltSorter.Core.Utilities.LoopTrack;

namespace Zeye.NarrowBeltSorter.Core.Options.LoopTrack {
    /// <summary>
    /// 雷码连接参数配置。
    /// </summary>
    public sealed record LoopTrackLeiMaConnectionOptions {
        /// <summary>
        /// 传输模式（可选值：TcpGateway/SerialRtu）。
        /// </summary>
        public string Transport { get; set; } = LoopTrackLeiMaTransportModes.TcpGateway;

        /// <summary>
        /// 远端地址（格式：{IP}:{Port}，示例：127.0.0.1:502；仅 TcpGateway 模式有效）。
        /// </summary>
        public string RemoteHost { get; set; } = "127.0.0.1:502";

        /// <summary>
        /// 串口 RTU 参数配置（仅 SerialRtu 模式有效）。
        /// </summary>
        public LoopTrackLeiMaSerialRtuOptions SerialRtu { get; set; } = new();

        /// <summary>
        /// Modbus 从站地址列表（每个值范围：1~247，至少配置一个）。
        /// </summary>
        public List<byte> SlaveAddresses { get; set; } = new();

        /// <summary>
        /// 多从站速度汇总策略（可选值：Min/Avg/Median）。
        /// </summary>
        public SpeedAggregateStrategy SpeedAggregateStrategy { get; set; } = SpeedAggregateStrategy.Min;

        /// <summary>
        /// 通讯超时（单位：ms，最小值：100，建议范围：500~5000）。
        /// </summary>
        public int TimeoutMs { get; set; } = 1000;

        /// <summary>
        /// 重试次数（不含首次，最小值：0，建议范围：1~5）。
        /// </summary>
        public int RetryCount { get; set; } = 2;

        /// <summary>
        /// 最高输出频率（单位：Hz，建议按雷码调机参数表与现场限速频率策略配置，最小值：0.1）。
        /// </summary>
        public decimal MaxOutputHz { get; set; } = 25m;

        /// <summary>
        /// P3.10 转矩给定最大原始值（范围：1~32767，通常配置为 1000）。
        /// </summary>
        public ushort MaxTorqueRawUnit { get; set; } = 1000;

        /// <summary>
        /// P3.10 写入最小间隔（单位：ms，最小值：50，建议范围：100~1000）。
        /// </summary>
        public int TorqueSetpointWriteIntervalMs { get; set; } = 300;
    }
}
