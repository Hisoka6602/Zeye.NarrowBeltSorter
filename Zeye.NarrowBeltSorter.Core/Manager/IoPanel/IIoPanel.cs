using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Events.IoPanel;

namespace Zeye.NarrowBeltSorter.Core.Manager.IoPanel {
    /// <summary>
    /// IoPanel（操作面板）管理器抽象，负责按钮电平边沿检测与按角色事件发布。
    /// </summary>
    public interface IIoPanel {
        /// <summary>
        /// 当前监控状态。
        /// </summary>
        IoPanelMonitoringStatus Status { get; }

        /// <summary>
        /// 是否正在监控。
        /// </summary>
        bool IsMonitoring { get; }

        /// <summary>
        /// 当前 IoPanel 需要注册到 EMC 的点位标识集合。
        /// 调用方在 <see cref="StartMonitoringAsync"/> 成功返回后读取该集合，保证可获得完整映射快照。
        /// </summary>
        IReadOnlyCollection<string> MonitoredPointIds { get; }

        /// <summary>
        /// 启动按钮按下事件（电平到达 TriggerState）。
        /// </summary>
        event EventHandler<IoPanelButtonPressedEventArgs>? StartButtonPressed;

        /// <summary>
        /// 停止按钮按下事件（电平到达 TriggerState）。
        /// </summary>
        event EventHandler<IoPanelButtonPressedEventArgs>? StopButtonPressed;

        /// <summary>
        /// 急停按钮按下事件（电平到达 TriggerState）。
        /// </summary>
        event EventHandler<IoPanelButtonPressedEventArgs>? EmergencyStopButtonPressed;

        /// <summary>
        /// 复位按钮按下事件（电平到达 TriggerState）。
        /// </summary>
        event EventHandler<IoPanelButtonPressedEventArgs>? ResetButtonPressed;

        /// <summary>
        /// 急停按钮释放事件（电平离开 TriggerState）。
        /// </summary>
        event EventHandler<IoPanelButtonReleasedEventArgs>? EmergencyStopButtonReleased;

        /// <summary>
        /// 监控状态变更事件。
        /// </summary>
        event EventHandler<IoPanelMonitoringStatusChangedEventArgs>? MonitoringStatusChanged;

        /// <summary>
        /// 异常事件（用于隔离异常，不影响上层调用链）。
        /// </summary>
        event EventHandler<IoPanelFaultedEventArgs>? Faulted;

        /// <summary>
        /// 启动监控。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        ValueTask StartMonitoringAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止监控。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        ValueTask StopMonitoringAsync(CancellationToken cancellationToken = default);
    }
}
