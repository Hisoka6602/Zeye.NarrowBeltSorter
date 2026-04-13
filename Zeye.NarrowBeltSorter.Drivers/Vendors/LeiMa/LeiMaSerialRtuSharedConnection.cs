using TouchSocket.Modbus;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa {
    /// <summary>
    /// 雷码串口 RTU 共享连接上下文。
    /// </summary>
    internal sealed class LeiMaSerialRtuSharedConnection {

        /// <summary>
        /// 初始化共享连接上下文。
        /// </summary>
        /// <param name="key">连接键。</param>
        /// <param name="portName">串口名称。</param>
        /// <param name="master">共享主站实例。</param>
        public LeiMaSerialRtuSharedConnection(string key, string portName, ModbusRtuMaster master) {
            Key = key;
            PortName = portName;
            Master = master;
        }

        /// <summary>
        /// 共享连接键。
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// 共享主站实例。
        /// </summary>
        public ModbusRtuMaster Master { get; }

        /// <summary>
        /// 串口名称。
        /// </summary>
        public string PortName { get; }

        /// <summary>
        /// 串口共享连接门控。
        /// </summary>
        public SemaphoreSlim Gate { get; } = new(1, 1);

        /// <summary>
        /// 引用计数与状态锁。
        /// </summary>
        public object SyncRoot { get; } = new();

        /// <summary>
        /// 是否已完成主站配置。
        /// </summary>
        public bool Configured { get; set; }

        /// <summary>
        /// 共享连接引用计数。
        /// </summary>
        public int RefCount { get; set; }
    }
}
