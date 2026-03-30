using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Events.IoPanel;

namespace Zeye.NarrowBeltSorter.Core.Manager.IoPanel {
    /// <summary>
    /// IoPanel（操作面板）管理器抽象，负责按钮电平边沿检测与事件发布。
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
        /// 按钮电平变更事件（包含点位、按钮名称、按钮角色、电平、时间戳等）。
        /// </summary>
        event EventHandler<IoPanelButtonStateChangedEventArgs>? ButtonStateChanged;

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
