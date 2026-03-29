using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.System {

    /// <summary>
    /// 传感器监控状态枚举。
    /// </summary>
    public enum SensorMonitoringStatus {

        /// <summary>
        /// 已停止
        /// </summary>
        [Description("已停止")]
        Stopped = 0,

        /// <summary>
        /// 监控中
        /// </summary>
        [Description("监控中")]
        Monitoring = 1,

        /// <summary>
        /// 异常
        /// </summary>
        [Description("异常")]
        Faulted = 2
    }
}
