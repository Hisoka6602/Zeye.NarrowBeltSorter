using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.Track {

    /// <summary>
    /// 速度汇总策略
    /// </summary>
    public enum SpeedAggregateStrategy {

        /// <summary>
        /// 取最小值（安全优先）
        /// </summary>
        [Description("最小值")]
        Min = 1,

        /// <summary>
        /// 取平均值
        /// </summary>
        [Description("平均值")]
        Average = 2
    }
}
