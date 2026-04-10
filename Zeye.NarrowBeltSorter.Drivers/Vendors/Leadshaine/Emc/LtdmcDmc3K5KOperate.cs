using System;

namespace csLTDMC {

    /// <summary>
    /// 雷赛 3K/5K 系列运动控制卡中断回调委托（对应 LTDMC.dll P/Invoke 绑定）。
    /// </summary>
    public delegate uint DMC3K5K_OPERATE(IntPtr operate_data);
}
