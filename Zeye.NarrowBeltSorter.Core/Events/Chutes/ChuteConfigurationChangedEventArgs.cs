namespace Zeye.NarrowBeltSorter.Core.Events.Chutes {
    /// <summary>
    /// 格口配置变更事件载荷
    /// </summary>
    public readonly record struct ChuteConfigurationChangedEventArgs {
        /// <summary>
        /// 配置快照（键：格口 Id；值：配置摘要）
        /// </summary>
        public required IReadOnlyDictionary<long, string> ConfigurationSnapshot { get; init; }

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
