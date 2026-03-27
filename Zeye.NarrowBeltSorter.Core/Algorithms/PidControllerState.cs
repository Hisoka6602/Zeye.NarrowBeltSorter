namespace Zeye.NarrowBeltSorter.Core.Algorithms {
    /// <summary>
    /// PID 控制器状态。
    /// </summary>
    /// <param name="Integral">积分累计值。</param>
    /// <param name="LastError">上一次误差（速度误差域）。</param>
    /// <param name="LastDerivative">上一次微分（速度误差变化率）。</param>
    /// <param name="Initialized">是否已完成初始化。</param>
    public readonly record struct PidControllerState(
        decimal Integral,
        decimal LastError,
        decimal LastDerivative,
        bool Initialized);
}
