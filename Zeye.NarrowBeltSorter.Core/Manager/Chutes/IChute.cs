using Zeye.NarrowBeltSorter.Core.Enums.Chutes;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Events.Chutes;
using Zeye.NarrowBeltSorter.Core.Models.Parcel;

namespace Zeye.NarrowBeltSorter.Core.Manager.Chutes {

    /// <summary>
    /// 格口接口（描述单个格口状态与控制能力）
    /// </summary>
    public interface IChute {
        /// <summary>
        /// 格口 Id
        /// </summary>
        long Id { get; }

        /// <summary>
        /// 格口名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 是否强排格口
        /// </summary>
        bool IsForced { get; }

        /// <summary>
        /// 格口状态
        /// </summary>
        ChuteStatus Status { get; }

        /// <summary>
        /// 是否目标格口
        /// </summary>
        bool IsTarget { get; }

        /// <summary>
        /// 等待落格包裹（无待落格时为 null）
        /// </summary>
        ParcelInfo? WaitingParcel { get; }

        /// <summary>
        /// 预计落格时间（本地时间语义，无计划时为 null）
        /// </summary>
        DateTime? ExpectedDropAt { get; }

        /// <summary>
        /// 已落格包裹集合（快照）
        /// </summary>
        IReadOnlyCollection<ParcelInfo> DroppedParcels { get; }

        /// <summary>
        /// 已落格总数
        /// </summary>
        long DroppedCount { get; }

        /// <summary>
        /// IO 状态
        /// </summary>
        IoState IoState { get; }

        /// <summary>
        /// 格口感应点距小车 IO 的小车数量
        /// </summary>
        int DistanceToCarrierIoCount { get; }

        /// <summary>
        /// 落格延迟补偿
        /// </summary>
        TimeSpan DropDelayCompensation { get; }

        /// <summary>
        /// 距离补偿映射（键：距离等级；值：补偿时长）
        /// </summary>
        IReadOnlyDictionary<ParcelToChuteDistanceLevel, TimeSpan> DistanceCompensationMap { get; }

        /// <summary>
        /// 最近一次已执行 IO 开闭时窗（本地时间语义，无记录时为 null）
        /// </summary>
        (DateTime OpenAt, DateTime CloseAt)? LastIoOpenCloseWindow { get; }

        /// <summary>
        /// 当前待执行 IO 开闭时窗（本地时间语义，无记录时为 null）
        /// </summary>
        (DateTime OpenAt, DateTime CloseAt)? PendingIoOpenCloseWindow { get; }

        /// <summary>
        /// 格口状态变更事件
        /// </summary>
        event EventHandler<ChuteStatusChangedEventArgs>? StatusChanged;

        /// <summary>
        /// 包裹落格事件
        /// </summary>
        event EventHandler<ChuteParcelDroppedEventArgs>? ParcelDropped;

        /// <summary>
        /// IO 状态变更事件
        /// </summary>
        event EventHandler<ChuteIoStateChangedEventArgs>? IoStateChanged;

        /// <summary>
        /// 落格延迟补偿变更事件
        /// </summary>
        event EventHandler<ChuteDropDelayCompensationChangedEventArgs>? DropDelayCompensationChanged;

        /// <summary>
        /// 距离补偿配置变更事件
        /// </summary>
        event EventHandler<ChuteDistanceCompensationChangedEventArgs>? DistanceCompensationChanged;

        /// <summary>
        /// 设置格口状态（设置失败或状态不允许变更时返回 false）
        /// </summary>
        ValueTask<bool> SetStatusAsync(
            ChuteStatus status,
            string? reason = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置待落格包裹（设置失败或状态不允许变更时返回 false）
        /// </summary>
        ValueTask<bool> SetWaitingParcelAsync(
            ParcelInfo? parcel,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置预计落格时间（设置失败或状态不允许变更时返回 false）
        /// </summary>
        ValueTask<bool> SetExpectedDropAtAsync(
            DateTime? expectedDropAt,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置格口感应点到小车 IO 的距离（设置失败或值非法时返回 false）
        /// </summary>
        ValueTask<bool> SetDistanceToCarrierIoCountAsync(
            int distanceToCarrierIoCount,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置落格延迟补偿（设置失败或状态不允许变更时返回 false）
        /// </summary>
        ValueTask<bool> SetDropDelayCompensationAsync(
            TimeSpan compensation,
            string? reason = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置距离补偿映射（设置失败或配置非法时返回 false）
        /// </summary>
        ValueTask<bool> SetDistanceCompensationAsync(
            IReadOnlyDictionary<ParcelToChuteDistanceLevel, TimeSpan> compensationMap,
            string? reason = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 执行落格（落格失败或状态不允许落格时返回 false）
        /// </summary>
        ValueTask<bool> DropAsync(
            ParcelInfo parcel,
            DateTime droppedAt,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 启用或禁用强排（切换失败或状态不允许切换时返回 false）
        /// </summary>
        ValueTask<bool> EnableForceOpenAsync(
            bool enabled,
            CancellationToken cancellationToken = default);
    }
}
