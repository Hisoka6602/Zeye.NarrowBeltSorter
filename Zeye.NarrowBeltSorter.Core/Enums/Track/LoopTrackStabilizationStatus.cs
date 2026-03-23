using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.Track {

    /// <summary>
    /// 轨道稳速状态
    /// </summary>
    public enum LoopTrackStabilizationStatus {

        /// <summary>
        /// 未稳速
        /// </summary>
        [Description("未稳速")]
        NotStabilized = 0,

        /// <summary>
        /// 稳速中
        /// </summary>
        [Description("稳速中")]
        Stabilizing = 1,

        /// <summary>
        /// 已稳速
        /// </summary>
        [Description("已稳速")]
        Stabilized = 2
    }
}
