namespace Zeye.NarrowBeltSorter.Core.Algorithms {
    /// <summary>
    /// PID 控制器输入。
    /// </summary>
    /// <param name="TargetSpeedMmps">目标速度（mm/s）。</param>
    /// <param name="ActualSpeedMmps">实际速度（mm/s）。</param>
    /// <param name="FreezeIntegral">是否冻结积分更新。</param>
    public readonly record struct PidControllerInput(
        decimal TargetSpeedMmps,
        decimal ActualSpeedMmps,
        bool FreezeIntegral);
}
