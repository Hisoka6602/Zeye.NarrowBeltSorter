using System;
using Zeye.NarrowBeltSorter.Core.Enums.Sorting;
using Zeye.NarrowBeltSorter.Core.Events.Carrier;

namespace Zeye.NarrowBeltSorter.Core.Manager.Carrier {

    /// <summary>
    /// 小车管理器（负责小车集合初始化、连接与调度控制）
    /// </summary>
    public interface ICarrierManager : IAsyncDisposable {

        /// <summary>
        /// 当前小车集合（快照）
        /// </summary>
        IReadOnlyCollection<ICarrier> Carriers { get; }

        /// <summary>
        /// 小车建环是否已完成
        /// </summary>
        bool IsRingBuilt { get; }

        /// <summary>
        /// 格口相对感应位小车偏移映射（键：格口 Id；值：偏移小车数量）
        /// </summary>
        IReadOnlyDictionary<long, int> ChuteCarrierOffsetMap { get; }

        /// <summary>
        /// 上车点相对感应位小车偏移
        /// </summary>
        int LoadingZoneCarrierOffset { get; }

        /// <summary>
        /// 当前落格模式
        /// </summary>
        DropMode DropMode { get; }

        /// <summary>
        /// 当前感应位小车 Id（未识别时为 null）
        /// </summary>
        long? CurrentInductionCarrierId { get; }

        /// <summary>
        /// 当前载货小车 Id 集合（快照）
        /// </summary>
        IReadOnlyCollection<long> LoadedCarrierIds { get; }

        /// <summary>
        /// 当前上货区小车 Id（未知时为 null）
        /// </summary>
        long? CurrentLoadingZoneCarrierId { get; }

        /// <summary>
        /// 小车建环完成事件
        /// </summary>
        event EventHandler<CarrierRingBuiltEventArgs>? RingBuilt;

        /// <summary>
        /// 当前感应位小车变更事件
        /// </summary>
        event EventHandler<CurrentInductionCarrierChangedEventArgs>? CurrentInductionCarrierChanged;

        /// <summary>
        /// 载货小车进入格口感应区事件
        /// </summary>
        event EventHandler<LoadedCarrierEnteredChuteInductionEventArgs>? LoadedCarrierEnteredChuteInduction;

        /// <summary>
        /// 小车载货状态变更事件
        /// </summary>
        event EventHandler<CarrierLoadStatusChangedEventArgs>? CarrierLoadStatusChanged;

        /// <summary>
        /// 小车靠近目标格口即将分拣事件
        /// </summary>
        event EventHandler<CarrierApproachingTargetChuteEventArgs>? CarrierApproachingTargetChute;

        /// <summary>
        /// 小车经过强排格口事件
        /// </summary>
        event EventHandler<CarrierPassedForcedChuteEventArgs>? CarrierPassedForcedChute;

        /// <summary>
        /// 小车连接状态变更事件
        /// </summary>
        event EventHandler<CarrierConnectionStatusChangedEventArgs>? CarrierConnectionStatusChanged;

        /// <summary>
        /// 管理器异常事件（用于隔离异常，不影响上层调用链）
        /// </summary>
        event EventHandler<CarrierManagerFaultedEventArgs>? Faulted;

        /// <summary>
        /// 连接管理器（连接失败或状态不允许连接时返回 false）
        /// </summary>
        ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 断开管理器连接（断开失败或状态不允许断开时返回 false）
        /// </summary>
        ValueTask<bool> DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 尝试获取小车快照（不存在返回 false）
        /// </summary>
        bool TryGetCarrier(long carrierId, out ICarrier carrier);

        /// <summary>
        /// 设置落格模式（设置失败或模式不允许切换时返回 false）
        /// </summary>
        ValueTask<bool> SetDropModeAsync(DropMode dropMode, CancellationToken cancellationToken = default);

        /// <summary>
        /// 更新建环结果（成功时会刷新 Carriers 并发布 RingBuilt 事件）。
        /// </summary>
        ValueTask<bool> BuildRingAsync(
            IReadOnlyCollection<long> carrierIds,
            string? message = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 更新当前感应位小车（更新失败或状态不允许更新时返回 false）。
        /// 传入 <paramref name="sensorOccurredAt"/> 时，<see cref="Core.Events.Carrier.CurrentInductionCarrierChangedEventArgs.ChangedAt"/>
        /// 将使用传感器触发时间而非 <see cref="DateTime.Now"/>，用于时间戳口径统一。
        /// </summary>
        ValueTask<bool> UpdateCurrentInductionCarrierAsync(
            long? carrierId,
            DateTime? sensorOccurredAt = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 发布“小车靠近目标格口即将分拣”事件（发布失败不抛异常，交由安全执行器隔离）
        /// </summary>
        ValueTask PublishCarrierApproachingTargetChuteAsync(
            CarrierApproachingTargetChuteEventArgs args,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 发布“小车经过强排格口”事件（发布失败不抛异常，交由安全执行器隔离）
        /// </summary>
        ValueTask PublishCarrierPassedForcedChuteAsync(
            CarrierPassedForcedChuteEventArgs args,
            CancellationToken cancellationToken = default);
    }
}
