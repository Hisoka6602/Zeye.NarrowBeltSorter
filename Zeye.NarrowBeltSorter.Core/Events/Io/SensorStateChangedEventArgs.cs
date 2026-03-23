namespace Zeye.NarrowBeltSorter.Core.Events.Io {
    /// <summary>
    /// 传感器电平变更事件载荷
    /// </summary>
    /// <param name="Point">IO 点位编号（物理/逻辑点序号）。</param>
    /// <param name="SensorName">传感器名称（用于日志/诊断/显示）。</param>
    /// <param name="SensorType">点位类型（输入/输出等）。</param>
    /// <param name="OldState">变更前电平状态。</param>
    /// <param name="NewState">变更后电平状态。</param>
    /// <param name="TriggerState">触发该事件所关注的目标电平（例如上升沿/下降沿对应的电平）。</param>
    /// <param name="OccurredAtMs">事件发生时间戳（毫秒，通常为相对系统启动或统一时基）。</param>
    public readonly record struct SensorStateChangedEventArgs(
        int Point,
        string SensorName,
        IoPointType SensorType,
        IoState OldState,
        IoState NewState,
        IoState TriggerState,
        long OccurredAtMs);
}
