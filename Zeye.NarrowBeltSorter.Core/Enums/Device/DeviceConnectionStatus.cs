using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.Device {

    /// <summary>
    /// 设备连接状态枚举。
    /// </summary>
    public enum DeviceConnectionStatus {

        /// <summary>
        /// 未连接。
        /// </summary>
        [Description("未连接")]
        Disconnected = 0,

        /// <summary>
        /// 连接中。
        /// </summary>
        [Description("连接中")]
        Connecting = 1,

        /// <summary>
        /// 已连接。
        /// </summary>
        [Description("已连接")]
        Connected = 2,

        /// <summary>
        /// 故障。
        /// </summary>
        [Description("故障")]
        Faulted = 3
    }
}
