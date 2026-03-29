using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.SiemensS7 {
    /// <summary>
    /// 西门子 S7 点位地址区枚举。
    /// </summary>
    public enum SiemensS7AddressArea {
        /// <summary>
        /// 输入区（I）。
        /// </summary>
        [Description("输入区")]
        Input = 0,

        /// <summary>
        /// 输出区（Q）。
        /// </summary>
        [Description("输出区")]
        Output = 1,

        /// <summary>
        /// 数据块区（DB）。
        /// </summary>
        [Description("数据块区")]
        DataBlock = 2,
    }
}
