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
        /// 首车传感器（1号小车）
        /// </summary>
        [Description("首车传感器")]
        FirstCarSensor = 2,

        /// <summary>
        /// 落格传感器
        /// </summary>
        [Description("落格传感器")]
        ChuteDropSensor = 3,

        /// <summary>
        /// 非首车传感器
        /// </summary>
        [Description("非首车传感器")]
        NonFirstCarSensor = 4,

        /// <summary>
        /// 异常件阻塞传感器
        /// </summary>
        [Description("异常件阻塞传感器")]
        AbnormalParcelBlockSensor = 5,

        /// <summary>
        /// 上车触发源传感器。
        /// </summary>
        [Description("上车触发源传感器")]
        LoadingTriggerSensor = 6,

        /// <summary>
        /// 检修开关传感器（打开时系统进入检修状态，关闭时恢复暂停状态）。
        /// </summary>
        [Description("检修开关传感器")]
        MaintenanceSwitchSensor = 7,
    }
}
