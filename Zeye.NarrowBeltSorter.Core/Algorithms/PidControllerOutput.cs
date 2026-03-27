namespace Zeye.NarrowBeltSorter.Core.Algorithms {
    /// <summary>
    /// PID 控制器输出。
    /// </summary>
    /// <param name="CommandOutput">建议输出控制量（控制域单位）。</param>
    /// <param name="ErrorSpeedMmps">速度误差（mm/s）。</param>
    /// <param name="Proportional">比例项贡献。</param>
    /// <param name="Integral">积分项贡献。</param>
    /// <param name="Derivative">微分项贡献。</param>
    /// <param name="UnclampedOutput">限幅前控制量。</param>
    /// <param name="OutputClamped">是否触发输出限幅。</param>
    /// <param name="NextState">下一控制状态。</param>
    public readonly record struct PidControllerOutput(
        decimal CommandOutput,
        decimal ErrorSpeedMmps,
        decimal Proportional,
        decimal Integral,
        decimal Derivative,
        decimal UnclampedOutput,
        bool OutputClamped,
        PidControllerState NextState);
}
