namespace csLTDMC {

    /// <summary>
    /// DA 曲线控制点结构体，用于 5X10 系列连续插补 DA 跟随输出功能（对应 LTDMC.dll P/Invoke 绑定）。
    /// </summary>
    public struct DaCurve_CtrlPoint {
        /// <summary>DA 电压输出值（因变量，单位：V，典型范围由硬件 DA 量程决定，参考 LTDMC 文档）</summary>
        public float vol_val;
        /// <summary>控制值（自变量，单位由厂商协议决定，具体范围参考 LTDMC 文档）</summary>
        public float ctrl_val;
    }
}
