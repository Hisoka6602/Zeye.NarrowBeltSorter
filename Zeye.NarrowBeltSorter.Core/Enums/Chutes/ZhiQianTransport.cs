using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.Chutes {

    /// <summary>
    /// 智嵌继电器通信传输模式枚举。
    /// </summary>
    public enum ZhiQianTransport {

        /// <summary>
        /// Modbus TCP（以太网连接，推荐优先使用）。
        /// </summary>
        [Description("Modbus TCP")]
        ModbusTcp = 0,

        /// <summary>
        /// Modbus RTU（RS485 串口连接）。
        /// </summary>
        [Description("Modbus RTU")]
        ModbusRtu = 1
    }
}
