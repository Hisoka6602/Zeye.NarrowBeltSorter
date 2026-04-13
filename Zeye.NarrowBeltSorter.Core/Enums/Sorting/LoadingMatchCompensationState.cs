using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.Sorting {

    /// <summary>
    /// 上车匹配时序补偿状态。
    /// </summary>
    public enum LoadingMatchCompensationState {

        /// <summary>
        /// 补偿已激活，已对目标上车小车编号应用 delta 偏移。
        /// </summary>
        [Description("已应用补偿")]
        Active,

        /// <summary>
        /// 降级为固定偏移，不应用 delta 补偿。
        /// </summary>
        [Description("降级为固定偏移")]
        Fallback
    }
}
