using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.Parcel {

    /// <summary>
    /// 包裹小车集合变更类型
    /// </summary>
    public enum ParcelCarriersChangeType {

        /// <summary>
        /// 绑定小车
        /// </summary>
        [Description("绑定小车")]
        Bound = 1,

        /// <summary>
        /// 解绑小车
        /// </summary>
        [Description("解绑小车")]
        Unbound = 2,

        /// <summary>
        /// 覆盖设置小车集合
        /// </summary>
        [Description("覆盖设置小车集合")]
        Replaced = 3,

        /// <summary>
        /// 清空小车集合
        /// </summary>
        [Description("清空小车集合")]
        Cleared = 4
    }
}
