namespace csLTDMC {
    /// <summary>
    /// 雷赛 SDK PWM 曲线控制点结构体（P/Invoke 绑定）。
    /// </summary>
    public struct PwmCurve_CtrlPoint {
        public float fl_val;   // pwm随动值（因变量）
        public float ctrl_val; // 控制值（自变量）
    }
}
