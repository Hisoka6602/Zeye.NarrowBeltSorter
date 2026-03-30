using Zeye.NarrowBeltSorter.Core.Enums.Io;

namespace Zeye.NarrowBeltSorter.Core.Events.IoPanel {
    /// <summary>
    /// IoPanel 监控状态变更事件载荷。
    /// </summary>
    /// <param name="OldStatus">变更前的监控状态。</param>
    /// <param name="NewStatus">变更后的监控状态。</param>
    /// <param name="Timestamp">状态变更发生时间（本地时间语义，DateTimeKind.Local）。</param>
    public readonly record struct IoPanelMonitoringStatusChangedEventArgs(
        IoPanelMonitoringStatus OldStatus,
        IoPanelMonitoringStatus NewStatus,
        DateTime Timestamp);
}
