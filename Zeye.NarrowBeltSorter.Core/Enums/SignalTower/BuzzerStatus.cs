using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.SignalTower {

    /// <summary>
    /// 信号塔蜂鸣器状态。
    /// </summary>
    public enum BuzzerStatus {
        /// <summary>
        /// 关闭。
        /// </summary>
        [Description("关闭")]
        Off = 0,

        /// <summary>
        /// 开启。
        /// </summary>
        [Description("开启")]
        On = 1
    }
}
