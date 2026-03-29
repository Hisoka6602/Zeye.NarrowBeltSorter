namespace Zeye.NarrowBeltSorter.Core.Options.SiemensS7 {
    /// <summary>
    /// 西门子 S7 传感器监控配置。
    /// </summary>
    public sealed record SiemensS7SensorOptions {

        /// <summary>
        /// 传感器名称。
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 绑定逻辑点位编号。
        /// </summary>
        public int PointId { get; set; }

        /// <summary>
        /// 去抖时间窗口（毫秒）。
        /// </summary>
        public int DebounceWindowMs { get; set; } = 50;
    }
}
