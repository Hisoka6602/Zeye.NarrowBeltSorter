namespace Zeye.NarrowBeltSorter.Core.Events.Chutes {
    /// <summary>
    /// 格口管理器连接状态变更事件载荷
    /// </summary>
    public readonly record struct ChuteManagerConnectionStatusChangedEventArgs {
        /// <summary>
        /// 变更前连接状态
        /// </summary>
        public required Zeye.NarrowBeltSorter.Core.Enums.Device.DeviceConnectionStatus OldStatus { get; init; }

        /// <summary>
        /// 变更后连接状态
        /// </summary>
        public required Zeye.NarrowBeltSorter.Core.Enums.Device.DeviceConnectionStatus NewStatus { get; init; }

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
