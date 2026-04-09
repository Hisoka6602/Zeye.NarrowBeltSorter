namespace Zeye.NarrowBeltSorter.Core.Options.LogCleanup {
    /// <summary>
    /// 日志清理服务配置。
    /// </summary>
    public record class LogCleanupSettings {
        /// <summary>
        /// 是否启用日志清理（取值：true/false）。
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 日志文件保留天数（最小值：1，建议范围：1~30）。
        /// </summary>
        public int RetentionDays { get; set; } = 2;

        /// <summary>
        /// 清理检查间隔（小时，最小值：1，建议范围：1~24）。
        /// </summary>
        public int CheckIntervalHours { get; set; } = 1;

        /// <summary>
        /// 日志根目录（相对或绝对路径，默认值：logs）。
        /// </summary>
        public string LogDirectory { get; set; } = "logs";
    }
}
