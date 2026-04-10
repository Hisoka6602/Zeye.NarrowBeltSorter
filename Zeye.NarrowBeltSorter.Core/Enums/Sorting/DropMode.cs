using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.Sorting {

    /// <summary>
    /// 落格配置方案
    /// </summary>
    public enum DropMode {

        /// <summary>
        /// 红外感应方案。
        /// </summary>
        [Description("红外")]
        Infrared = 1,

        /// <summary>
        /// 漏波电缆方案。
        /// </summary>
        [Description("漏波电缆")]
        LeakyCable = 2,

        /// <summary>
        /// 无线 WiFi 方案。
        /// </summary>
        [Description("无线WiFi")]
        WirelessWifi = 3
    }
}
