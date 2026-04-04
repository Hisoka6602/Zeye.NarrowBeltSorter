using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.Io {

    /// <summary>
    /// IoPanel 按钮角色类型。
    /// </summary>
    public enum IoPanelButtonType {

        /// <summary>
        /// 未指定角色。
        /// </summary>
        [Description("未指定")]
        Unspecified = 0,

        /// <summary>
        /// 启动角色。
        /// </summary>
        [Description("启动")]
        Start = 1,

        /// <summary>
        /// 停止角色。
        /// </summary>
        [Description("停止")]
        Stop = 2,

        /// <summary>
        /// 急停角色。
        /// </summary>
        [Description("急停")]
        EmergencyStop = 3,

        /// <summary>
        /// 复位角色。
        /// </summary>
        [Description("复位")]
        Reset = 4,

        /// <summary>
        /// 检修开关角色：打开时系统进入检修状态，关闭时停止轨道并切换至暂停状态。
        /// </summary>
        [Description("检修开关")]
        MaintenanceSwitch = 5
    }
}
