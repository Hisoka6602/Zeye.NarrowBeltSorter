using Zeye.NarrowBeltSorter.Core.Enums.SignalTower;

namespace Zeye.NarrowBeltSorter.Core.Events.SignalTower {
    /// <summary>
    /// 信号塔三色灯状态变更事件载荷
    /// </summary>
    public readonly record struct SignalTowerLightStatusChangedEventArgs {
        /// <summary>
        /// 信号塔 Id
        /// </summary>
        public required long SignalTowerId { get; init; }

        /// <summary>
        /// 变更前状态
        /// </summary>
        public required SignalTowerLightStatus OldStatus { get; init; }

        /// <summary>
        /// 变更后状态
        /// </summary>
        public required SignalTowerLightStatus NewStatus { get; init; }

        /// <summary>
        /// 变更时间（本地时间语义）
        /// </summary>
        public required DateTime ChangedAt { get; init; }
    }
}
