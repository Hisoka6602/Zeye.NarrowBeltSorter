using Zeye.NarrowBeltSorter.Core.Enums.Io;

namespace Zeye.NarrowBeltSorter.Core.Events.IoPanel {
    /// <summary>
    /// IoPanel 按钮电平变更事件载荷。
    /// </summary>
    /// <param name="PointId">IO 点位编号（逻辑点位 ID）。</param>
    /// <param name="ButtonName">按钮名称（用于日志/诊断/显示）。</param>
    /// <param name="ButtonType">按钮角色类型（启动/停止/急停/复位等）。</param>
    /// <param name="OldState">变更前电平状态。</param>
    /// <param name="NewState">变更后电平状态。</param>
    /// <param name="OccurredAt">事件发生时间（本地时间语义，DateTimeKind.Local）。</param>
    public readonly record struct IoPanelButtonStateChangedEventArgs(
        string PointId,
        string ButtonName,
        IoPanelButtonType ButtonType,
        IoState OldState,
        IoState NewState,
        DateTime OccurredAt);
}
