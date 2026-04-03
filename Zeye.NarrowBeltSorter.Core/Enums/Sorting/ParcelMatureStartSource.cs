using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.Sorting {

    /// <summary>
    /// 包裹成熟时间起始来源。
    /// </summary>
    public enum ParcelMatureStartSource {

        /// <summary>
        /// 使用创建包裹触发源作为成熟计时起点。
        /// </summary>
        [Description("创建包裹触发源")]
        ParcelCreateSensor = 1,

        /// <summary>
        /// 使用上车触发源作为成熟计时起点。
        /// </summary>
        [Description("上车触发源")]
        LoadingTriggerSensor = 2,
    }
}
