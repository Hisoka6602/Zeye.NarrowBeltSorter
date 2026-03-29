using Zeye.NarrowBeltSorter.Core.Enums.SignalTower;

namespace Zeye.NarrowBeltSorter.Core.Events.SignalTower {
    /// <summary>
    /// 信号塔蜂鸣器状态变更事件载荷
    /// </summary>
    public readonly record struct SignalTowerBuzzerStatusChangedEventArgs {
        /// <summary>
        /// 信号塔 Id
        /// </summary>
        public required long SignalTowerId { get; init; }

        /// <summary>
        /// 变更前状态
        /// </summary>
        public required BuzzerStatus OldStatus { get; init; }

        /// <summary>
        /// 变更后状态
        /// </summary>
        public required BuzzerStatus NewStatus { get; init; }

        /// <summary>
        /// 变更时间（本地时间语义）
        /// </summary>
        public required DateTime ChangedAt { get; init; }
    }
}
