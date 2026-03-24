namespace Zeye.NarrowBeltSorter.Core.Algorithms {
    /// <summary>
    /// PID 控制器输出。
    /// </summary>
    /// <param name="CommandHz">建议输出频率（Hz）。</param>
    /// <param name="ErrorSpeedMmps">速度误差（mm/s）。</param>
    /// <param name="Proportional">比例项贡献。</param>
    /// <param name="Integral">积分项贡献。</param>
    /// <param name="Derivative">微分项贡献。</param>
    /// <param name="UnclampedHz">限幅前频率（Hz）。</param>
    /// <param name="OutputClamped">是否触发输出限幅。</param>
    /// <param name="NextState">下一控制状态。</param>
    public readonly record struct PidControllerOutput(
        decimal CommandHz,
        decimal ErrorSpeedMmps,
        decimal Proportional,
        decimal Integral,
        decimal Derivative,
        decimal UnclampedHz,
        bool OutputClamped,
        PidControllerState NextState);
}
