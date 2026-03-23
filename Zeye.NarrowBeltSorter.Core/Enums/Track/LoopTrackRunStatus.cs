using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.Track {

    /// <summary>
    /// 轨道运行状态
    /// </summary>
    public enum LoopTrackRunStatus {

        /// <summary>
        /// 停止
        /// </summary>
        [Description("停止")]
        Stopped = 0,

        /// <summary>
        /// 运行中
        /// </summary>
        [Description("运行中")]
        Running = 1,

        /// <summary>
        /// 故障
        /// </summary>
        [Description("故障")]
        Faulted = 2
    }
}
