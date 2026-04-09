using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.Chutes {

    /// <summary>
    /// 包裹离格口距离等级枚举。
    /// </summary>
    public enum ParcelToChuteDistanceLevel {

        /// <summary>
        /// 远。
        /// </summary>
        [Description("远")]
        Far = 1,

        /// <summary>
        /// 中。
        /// </summary>
        [Description("中")]
        Medium = 2,

        /// <summary>
        /// 近。
        /// </summary>
        [Description("近")]
        Near = 3
    }
}
