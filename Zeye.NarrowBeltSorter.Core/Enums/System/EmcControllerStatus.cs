using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.System {

    /// <summary>
    /// EMC 控制器监控状态枚举。
    /// </summary>
    public enum EmcControllerStatus {

        /// <summary>
        /// 已停止。
        /// </summary>
        [Description("已停止")]
        Stopped = 0,

        /// <summary>
        /// 初始化中。
        /// </summary>
        [Description("初始化中")]
        Initializing = 1,

        /// <summary>
        /// 运行中。
        /// </summary>
        [Description("运行中")]
        Running = 2,

        /// <summary>
        /// 重连中。
        /// </summary>
        [Description("重连中")]
        Reconnecting = 3,

        /// <summary>
        /// 异常。
        /// </summary>
        [Description("异常")]
        Faulted = 4
    }
}
