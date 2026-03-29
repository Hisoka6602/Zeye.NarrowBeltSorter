namespace csLTDMC {
    /// <summary>
    /// 雷赛 SDK DA 曲线控制点结构体（P/Invoke 绑定）。
    /// </summary>
    public struct DaCurve_CtrlPoint {
        public float vol_val;  // da值（因变量）
        public float ctrl_val; // 控制值（自变量）
    }
}
