// 抑制厂商 SDK 结构体的未使用字段警告（CS0169）。
// 字段 start_pos、interval、count 由底层 LTDMC.dll 直接访问，C# 代码不显式引用。
#pragma warning disable CS0169

namespace csLTDMC {

    /// <summary>
    /// 雷赛高速比较功能的位置比较信息结构体（对应 LTDMC.dll P/Invoke 绑定）。
    /// </summary>
    public struct struct_hs_cmp_info {
        /// <summary>线性比较起始点位置（单位：脉冲或用户单位，具体含义参考 LTDMC 文档）</summary>
        private double start_pos;
        /// <summary>比较间距（单位：脉冲或用户单位，最小值：大于 0，具体参考 LTDMC 文档）</summary>
        private double interval;
        /// <summary>比较次数（最小值：1，上限由硬件缓存决定，参考 LTDMC 文档）</summary>
        private int count;
    }
}
