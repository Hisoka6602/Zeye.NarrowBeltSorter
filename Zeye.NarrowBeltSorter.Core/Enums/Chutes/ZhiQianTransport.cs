using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.Chutes {

    /// <summary>
    /// 智嵌继电器通信传输模式枚举。
    /// </summary>
    public enum ZhiQianTransport {

        /// <summary>
        /// 普通 TCP（以太网连接，ASCII 协议，手册 7.2 节，推荐优先使用）。
        /// </summary>
        [Description("TCP (ASCII)")]
        Tcp = 0,

        /// <summary>
        /// Modbus RTU（RS485 串口连接）。
        /// </summary>
        [Description("Modbus RTU")]
        ModbusRtu = 1
    }
}
