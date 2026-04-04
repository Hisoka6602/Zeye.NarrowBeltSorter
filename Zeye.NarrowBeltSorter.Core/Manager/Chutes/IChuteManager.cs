using Zeye.NarrowBeltSorter.Core.Events.Chutes;

namespace Zeye.NarrowBeltSorter.Core.Manager.Chutes {

    /// <summary>
    /// 格口管理器（负责格口集合管理、连接管理与配置分发）
    /// </summary>
    public interface IChuteManager : IAsyncDisposable {
        /// <summary>
        /// 格口集合（快照）
        /// </summary>
        IReadOnlyCollection<IChute> Chutes { get; }

        /// <summary>
        /// 当前强排格口 Id（未设置时为 null）
        /// </summary>
        long? ForcedChuteId { get; }

        /// <summary>
        /// 目标格口 Id 集合（快照）
        /// </summary>
        IReadOnlySet<long> TargetChuteIds { get; }

        /// <summary>
        /// 格口配置快照（键：格口 Id；值：配置摘要）
        /// </summary>
        IReadOnlyDictionary<long, string> ChuteConfigurationSnapshot { get; }

        /// <summary>
        /// 锁格格口 Id 集合（快照）
        /// </summary>
        IReadOnlySet<long> LockedChuteIds { get; }

        /// <summary>
        /// 当前连接状态
        /// </summary>
        Zeye.NarrowBeltSorter.Core.Enums.Device.DeviceConnectionStatus ConnectionStatus { get; }

        /// <summary>
        /// 包裹落格事件
        /// </summary>
        event EventHandler<ChuteParcelDroppedEventArgs>? ParcelDropped;

        /// <summary>
        /// 强排格口变更事件
        /// </summary>
        event EventHandler<ForcedChuteChangedEventArgs>? ForcedChuteChanged;

        /// <summary>
        /// 格口配置变更事件
        /// </summary>
        event EventHandler<ChuteConfigurationChangedEventArgs>? ChuteConfigurationChanged;

        /// <summary>
        /// 锁格状态变更事件
        /// </summary>
        event EventHandler<ChuteLockStatusChangedEventArgs>? ChuteLockStatusChanged;

        /// <summary>
        /// 连接状态变更事件
        /// </summary>
        event EventHandler<ChuteManagerConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

        /// <summary>
        /// 管理器异常事件（用于隔离异常，不影响上层调用链）
        /// </summary>
        event EventHandler<ChuteManagerFaultedEventArgs>? Faulted;

        /// <summary>
        /// 连接管理器（连接失败或状态不允许连接时返回 false）
        /// 步骤：建立设备会话并同步连接状态
        /// </summary>
        ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 断开管理器连接（断开失败或状态不允许断开时返回 false）
        /// </summary>
        ValueTask<bool> DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置强排格口（设置失败或状态不允许切换时返回 false）
        /// </summary>
        ValueTask<bool> SetForcedChuteAsync(long? chuteId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量设置强排格口集合（集合内闭合、集合外断开；设置失败或状态不允许切换时返回 false）。
        /// </summary>
        ValueTask<bool> SetForcedChuteSetAsync(IReadOnlyCollection<long> chuteIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// 添加目标格口（添加失败或目标不允许添加时返回 false）
        /// </summary>
        ValueTask<bool> AddTargetChuteAsync(long chuteId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 移除目标格口（移除失败或目标不存在时返回 false）
        /// </summary>
        ValueTask<bool> RemoveTargetChuteAsync(long chuteId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置锁格状态（设置失败或状态不允许变更时返回 false）
        /// </summary>
        ValueTask<bool> SetChuteLockedAsync(
            long chuteId,
            bool isLocked,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 尝试获取格口快照（不存在返回 false）
        /// </summary>
        bool TryGetChute(long chuteId, out IChute chute);
    }
}
