using Zeye.NarrowBeltSorter.Core.Enums.Io;

namespace Zeye.NarrowBeltSorter.Core.Events.Chutes {
    /// <summary>
    /// 格口 IO 状态变更事件载荷
    /// </summary>
    public readonly record struct ChuteIoStateChangedEventArgs {
        /// <summary>
        /// 格口 Id
        /// </summary>
        public required long ChuteId { get; init; }

        /// <summary>
        /// 变更前 IO 状态
        /// </summary>
        public required IoState OldState { get; init; }

        /// <summary>
        /// 变更后 IO 状态
        /// </summary>
        public required IoState NewState { get; init; }

        /// <summary>
        /// 变更时间（本地时间语义）
        /// </summary>
        public required DateTime ChangedAt { get; init; }

        /// <summary>
        /// 变更原因（可空）
        /// </summary>
        public string? Reason { get; init; }
    }
}
