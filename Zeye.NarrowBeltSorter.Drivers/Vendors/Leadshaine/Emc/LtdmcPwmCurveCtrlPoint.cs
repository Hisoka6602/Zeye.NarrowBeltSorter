namespace csLTDMC {

    /// <summary>
    /// PWM 曲线控制点结构体，用于 5X10 系列连续插补 PWM 跟随输出功能（对应 LTDMC.dll P/Invoke 绑定）。
    /// </summary>
    public struct PwmCurve_CtrlPoint {
        /// <summary>PWM 随动值（因变量，单位：%，典型范围 0~100）</summary>
        public float fl_val;
        /// <summary>控制值（自变量，单位由厂商协议决定，具体范围参考 LTDMC 文档）</summary>
        public float ctrl_val;
    }
}
