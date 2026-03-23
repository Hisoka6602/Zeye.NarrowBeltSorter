using Zeye.NarrowBeltSorter.Core.Enums.System;

namespace Zeye.NarrowBeltSorter.Core.Events.Io {
    /// <summary>
    /// 传感器监控状态变更事件载荷。
    /// </summary>
    /// <remarks>
    /// 用于在监控状态发生切换时，携带“变更前/变更后”的状态信息及变更发生的时间点。
    /// </remarks>
    /// <param name="OldStatus">
    /// 变更前的监控状态。
    /// </param>
    /// <param name="NewStatus">
    /// 变更后的监控状态。
    /// </param>
    /// <param name="Timestamp">状态变更发生时间（本地时间语义）。</param>
    public readonly record struct SensorMonitoringStatusChangedEventArgs(
        SensorMonitoringStatus OldStatus,
        SensorMonitoringStatus NewStatus,
        DateTime Timestamp);
}
