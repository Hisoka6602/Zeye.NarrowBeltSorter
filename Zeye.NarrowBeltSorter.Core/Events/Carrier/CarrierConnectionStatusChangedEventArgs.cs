using Zeye.NarrowBeltSorter.Core.Enums.Device;

namespace Zeye.NarrowBeltSorter.Core.Events.Carrier {
    /// <summary>
    /// 小车连接状态变更事件载荷
    /// </summary>
    public readonly record struct CarrierConnectionStatusChangedEventArgs {
        /// <summary>
        /// 小车 Id
        /// </summary>
        public required long CarrierId { get; init; }

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

        /// <summary>
        /// 备注（可空）
        /// </summary>
        public string? Message { get; init; }
    }
}
