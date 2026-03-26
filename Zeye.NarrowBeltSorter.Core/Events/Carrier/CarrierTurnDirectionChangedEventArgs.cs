using Zeye.NarrowBeltSorter.Core.Enums.Carrier;

namespace Zeye.NarrowBeltSorter.Core.Events.Carrier {
    /// <summary>
    /// 小车转向变更事件载荷
    /// </summary>
    public readonly record struct CarrierTurnDirectionChangedEventArgs {
        /// <summary>
        /// 小车 Id
        /// </summary>
        public required long CarrierId { get; init; }

        /// <summary>
        /// 变更前转向
        /// </summary>
        public required CarrierTurnDirection OldDirection { get; init; }

        /// <summary>
        /// 变更后转向
        /// </summary>
        public required CarrierTurnDirection NewDirection { get; init; }

        /// <summary>
        /// 变更时间（本地时间语义）
        /// </summary>
        public required DateTime ChangedAt { get; init; }
    }
}
