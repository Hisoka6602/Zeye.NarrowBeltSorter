using System.IO.Ports;

namespace Zeye.NarrowBeltSorter.Host.Options.LoopTrack {
    /// <summary>
    /// LeiMa 串口 RTU 参数配置。
    /// </summary>
    public sealed record LoopTrackLeiMaSerialRtuOptions {
        /// <summary>
        /// 串口名称（示例：COM3）。
        /// </summary>
        public string PortName { get; set; } = "COM1";

        /// <summary>
        /// 波特率。
        /// </summary>
        public int BaudRate { get; set; } = 19200;

        /// <summary>
        /// 校验位。
        /// </summary>
        public Parity Parity { get; set; } = Parity.None;

        /// <summary>
        /// 数据位。
        /// </summary>
        public int DataBits { get; set; } = 8;

        /// <summary>
        /// 停止位。
        /// </summary>
        public StopBits StopBits { get; set; } = StopBits.One;
    }
}
