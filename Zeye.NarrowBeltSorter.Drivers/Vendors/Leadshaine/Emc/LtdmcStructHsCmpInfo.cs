// 抑制厂商 SDK 结构体的未使用字段警告（CS0169）。
// 此结构体来自雷赛 LTDMC.dll P/Invoke 绑定代码，字段由底层 DLL 直接访问，C# 代码不显式引用。
#pragma warning disable CS0169

namespace csLTDMC {
    /// <summary>
    /// 雷赛 SDK 线性比较信息结构体（P/Invoke 绑定）。
    /// </summary>
    public struct struct_hs_cmp_info {
        private double start_pos;   // 线性比较起始点位置
        private double interval;    // 间距
        private int count;          // 个数
    }
}
