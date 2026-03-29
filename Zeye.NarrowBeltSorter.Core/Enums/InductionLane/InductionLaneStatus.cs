using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.InductionLane {

    /// <summary>
    /// 供包台状态。
    /// </summary>
    public enum InductionLaneStatus {
        /// <summary>
        /// 未启动。
        /// </summary>
        [Description("未启动")]
        Stopped = 0,

        /// <summary>
        /// 启动中。
        /// </summary>
        [Description("启动中")]
        Starting = 1,

        /// <summary>
        /// 运行中。
        /// </summary>
        [Description("运行中")]
        Running = 2,

        /// <summary>
        /// 停止中。
        /// </summary>
        [Description("停止中")]
        Stopping = 3,

        /// <summary>
        /// 故障。
        /// </summary>
        [Description("故障")]
        Faulted = 4
    }
}
