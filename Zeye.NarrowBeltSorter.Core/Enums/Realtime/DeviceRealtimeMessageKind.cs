using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.Realtime {

    /// <summary>
    /// 设备实时消息类型
    /// </summary>
    public enum DeviceRealtimeMessageKind {

        /// <summary>
        /// 轨道状态
        /// </summary>
        [Description("轨道状态")]
        Track = 1,

        /// <summary>
        /// 传感器状态
        /// </summary>
        [Description("传感器状态")]
        Sensor = 2,

        /// <summary>
        /// 格口状态
        /// </summary>
        [Description("格口状态")]
        Chute = 3,

        /// <summary>
        /// 小车状态
        /// </summary>
        [Description("小车状态")]
        Carrier = 4,

        /// <summary>
        /// 驱动/变频器状态
        /// </summary>
        [Description("驱动状态")]
        Drive = 5,

        /// <summary>
        /// 系统汇总状态
        /// </summary>
        [Description("系统状态")]
        System = 9
    }
}
