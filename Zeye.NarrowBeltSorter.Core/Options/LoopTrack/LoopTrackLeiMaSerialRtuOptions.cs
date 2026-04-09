using System.IO.Ports;

namespace Zeye.NarrowBeltSorter.Core.Options.LoopTrack {
    /// <summary>
    /// LeiMa 串口 RTU 参数配置。
    /// </summary>
    public sealed record LoopTrackLeiMaSerialRtuOptions {
        /// <summary>
        /// 串口名称（示例：COM3；Linux 下格式：/dev/ttyUSB0）。
        /// </summary>
        public string PortName { get; set; } = "COM1";

        /// <summary>
        /// 波特率（常用值：9600/19200/38400/57600/115200）。
        /// </summary>
        public int BaudRate { get; set; } = 19200;

        /// <summary>
        /// 校验位（可选值：None/Odd/Even/Mark/Space）。
        /// </summary>
        public Parity Parity { get; set; } = Parity.None;

        /// <summary>
        /// 数据位（可选值：5/6/7/8，Modbus RTU 标准值为 8）。
        /// </summary>
        public int DataBits { get; set; } = 8;

        /// <summary>
        /// 停止位（可选值：None/One/Two/OnePointFive，Modbus RTU 标准值为 One）。
        /// </summary>
        public StopBits StopBits { get; set; } = StopBits.One;
    }
}
