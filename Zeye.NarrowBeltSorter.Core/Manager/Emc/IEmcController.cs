using Zeye.NarrowBeltSorter.Core.Enums.Emc;
using Zeye.NarrowBeltSorter.Core.Events.Emc;
using Zeye.NarrowBeltSorter.Core.Models.Emc;

namespace Zeye.NarrowBeltSorter.Core.Manager.Emc {
    /// <summary>
    /// EMC 控制器抽象（初始化、监控、写入、重连）。
    /// </summary>
    public interface IEmcController : IAsyncDisposable {
        /// <summary>
        /// 当前 EMC 状态。
        /// </summary>
        EmcControllerStatus Status { get; }

        /// <summary>
        /// 当前故障码（无故障时为 0）。
        /// </summary>
        int FaultCode { get; }

        /// <summary>
        /// 已注册监控点位快照。
        /// </summary>
        IReadOnlyCollection<IoPointInfo> MonitoredIoPoints { get; }

        /// <summary>
        /// 状态变化事件。
        /// </summary>
        event EventHandler<EmcStatusChangedEventArgs>? StatusChanged;

        /// <summary>
        /// 故障事件。
        /// </summary>
        event EventHandler<EmcFaultedEventArgs>? Faulted;

        /// <summary>
        /// 初始化完成事件。
        /// </summary>
        event EventHandler<EmcInitializedEventArgs>? Initialized;

        /// <summary>
        /// 初始化控制器。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否初始化成功。</returns>
        ValueTask<bool> InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 执行重连。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否重连成功。</returns>
        ValueTask<bool> ReconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 增量注册监控点位。
        /// </summary>
        /// <param name="pointIds">点位标识集合。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否注册成功。</returns>
        ValueTask<bool> SetMonitoredIoPointsAsync(
            IReadOnlyCollection<string> pointIds,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 尝试获取指定点位的最新快照（无全量克隆，适合高频轮询路径）。
        /// </summary>
        /// <param name="pointId">点位标识。</param>
        /// <param name="info">点位快照；未注册时为 default。</param>
        /// <returns>点位已注册且存在快照时返回 true，否则返回 false。</returns>
        bool TryGetMonitoredPoint(string pointId, out IoPointInfo info);

        /// <summary>
        /// 写入输出点位。
        /// </summary>
        /// <param name="pointId">点位标识。</param>
        /// <param name="value">输出值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否写入成功。</returns>
        ValueTask<bool> WriteIoAsync(
            string pointId,
            bool value,
            CancellationToken cancellationToken = default);
    }
}
