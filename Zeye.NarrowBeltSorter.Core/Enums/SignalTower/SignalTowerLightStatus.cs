using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.SignalTower {

    /// <summary>
    /// 信号塔三色灯状态。
    /// </summary>
    public enum SignalTowerLightStatus {
        /// <summary>
        /// 全灭。
        /// </summary>
        [Description("全灭")]
        Off = 0,

        /// <summary>
        /// 红灯亮。
        /// </summary>
        [Description("红灯亮")]
        Red = 1,

        /// <summary>
        /// 黄灯亮。
        /// </summary>
        [Description("黄灯亮")]
        Yellow = 2,

        /// <summary>
        /// 绿灯亮。
        /// </summary>
        [Description("绿灯亮")]
        Green = 3
    }
}
