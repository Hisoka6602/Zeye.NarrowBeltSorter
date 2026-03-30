using Zeye.NarrowBeltSorter.Core.Enums.Io;

namespace Zeye.NarrowBeltSorter.Core.Events.IoPanel {
    /// <summary>
    /// IoPanel 按钮释放事件载荷（电平离开 TriggerState 时触发，当前仅急停按钮使用）。所有时间字段使用本地时间语义。
    /// </summary>
    /// <param name="PointId">IO 点位编号（逻辑点位 ID）。</param>
    /// <param name="ButtonName">按钮名称（用于日志/诊断/显示）。</param>
    /// <param name="ButtonType">按钮角色类型（当前固定为 EmergencyStop）。</param>
    /// <param name="Timestamp">事件发生时间（本地时间语义，DateTimeKind.Local）。</param>
    public readonly record struct IoPanelButtonReleasedEventArgs(
        string PointId,
        string ButtonName,
        IoPanelButtonType ButtonType,
        DateTime Timestamp);
}
