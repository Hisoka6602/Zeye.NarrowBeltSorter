using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.Chutes {

    /// <summary>
    /// 红外控制模式
    /// </summary>
    public enum InfraredControlMode {

        /// <summary>
        /// 时间模式
        /// </summary>
        [Description("时间模式")]
        Time = 0,

        /// <summary>
        /// 位置模式
        /// </summary>
        [Description("位置模式")]
        Position = 1
    }
}
