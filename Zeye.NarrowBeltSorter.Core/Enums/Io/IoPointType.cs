using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.Io {

    /// <summary>
    /// IO 点位类型枚举
    /// </summary>
    public enum IoPointType {

        /// <summary>
        /// IO 面板按钮
        /// </summary>
        [Description("IO面板按钮")]
        PanelButton = 0,

        /// <summary>
        /// 创建包裹传感器
        /// </summary>
        [Description("创建包裹传感器")]
        ParcelCreateSensor = 1,

        /// <summary>
        /// 落格传感器
        /// </summary>
        [Description("落格传感器")]
        ChuteDropSensor = 3,
    }
}
