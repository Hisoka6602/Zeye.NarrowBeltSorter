using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.Emc;
using Zeye.NarrowBeltSorter.Core.Models.Emc;

namespace Zeye.NarrowBeltSorter.Core.Manager.Emc {
    /// <summary>
    /// EMC 控制器抽象（统一管理 IO 监控快照、点位写入与连接状态）。
    /// </summary>
    public interface IEmcController {

        /// <summary>
        /// 当前控制器状态。
        /// </summary>
        EmcControllerStatus Status { get; }

        /// <summary>
        /// 当前故障码（无故障时为空字符串）。
        /// </summary>
        string FaultCode { get; }

        /// <summary>
        /// 当前监控 IO 点位快照集合。
        /// </summary>
        IReadOnlyList<IoPointInfo> MonitoredIoPoints { get; }

        /// <summary>
        /// 控制器状态变化事件。
        /// </summary>
        event EventHandler<EmcStatusChangedEventArgs>? StatusChanged;

        /// <summary>
        /// 控制器故障事件。
        /// </summary>
        event EventHandler<EmcFaultedEventArgs>? Faulted;

        /// <summary>
        /// 控制器初始化完成事件。
        /// </summary>
        event EventHandler<EmcInitializedEventArgs>? Initialized;

        /// <summary>
        /// 初始化 EMC 控制器。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        ValueTask InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 触发 EMC 控制器重连流程。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        ValueTask ReconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置 EMC 监控点位集合。
        /// </summary>
        /// <param name="ioPoints">监控点位集合。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        ValueTask SetMonitoredIoPointsAsync(IReadOnlyList<IoPointInfo> ioPoints, CancellationToken cancellationToken = default);

        /// <summary>
        /// 按点位写入 IO 电平。
        /// </summary>
        /// <param name="point">点位编号。</param>
        /// <param name="state">目标电平。</param>
        /// <param name="writeContext">写入上下文（用于日志链路诊断）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>写入是否成功。</returns>
        ValueTask<bool> WriteIoAsync(int point, IoState state, string? writeContext = null, CancellationToken cancellationToken = default);
    }
}
