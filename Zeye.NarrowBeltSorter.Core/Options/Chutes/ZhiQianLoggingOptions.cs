namespace Zeye.NarrowBeltSorter.Core.Options.Chutes {

    /// <summary>
    /// 智嵌格口驱动日志配置（对应 appsettings Chutes:ZhiQian:Logging 节点）。
    /// 控制格口状态、通信与异常日志的落盘目录、保留天数与开关。
    /// </summary>
    public sealed record ZhiQianLoggingOptions {

        /// <summary>
        /// 是否启用格口分类文件日志（false 时仅输出到控制台与全局目标，取值：true/false）。
        /// </summary>
        public bool EnableCategoryFile { get; init; } = true;

        /// <summary>
        /// 格口分类日志目录（相对或绝对路径，默认 logs/chutes）。
        /// </summary>
        public string CategoryLogDirectory { get; init; } = "logs/chutes";

        /// <summary>
        /// 格口分类日志文件保留天数（建议范围：1~365，默认 7 天）。
        /// </summary>
        public int CategoryRetentionDays { get; init; } = 7;
    }
}
