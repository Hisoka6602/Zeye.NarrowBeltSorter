using Zeye.NarrowBeltSorter.Core.Enums.Device;

namespace Zeye.NarrowBeltSorter.Core.Events.SignalTower {
    /// <summary>
    /// 信号塔连接状态变更事件载荷
    /// </summary>
    public readonly record struct SignalTowerConnectionStatusChangedEventArgs {
        /// <summary>
        /// 信号塔 Id
        /// </summary>
        public required long SignalTowerId { get; init; }

        /// <summary>
        /// 变更前连接状态
        /// </summary>
        public required DeviceConnectionStatus OldStatus { get; init; }

        /// <summary>
        /// 变更后连接状态
        /// </summary>
        public required DeviceConnectionStatus NewStatus { get; init; }

        /// <summary>
        /// 变更时间（本地时间语义）
        /// </summary>
        public required DateTime ChangedAt { get; init; }
    }
}
