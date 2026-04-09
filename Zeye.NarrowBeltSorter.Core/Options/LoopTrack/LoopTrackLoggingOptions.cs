namespace Zeye.NarrowBeltSorter.Core.Options.LoopTrack {
    /// <summary>
    /// 环形轨道日志配置。
    /// </summary>
    public sealed record LoopTrackLoggingOptions {
        /// <summary>
        /// 控制台日志最小级别（可选值：Trace/Debug/Information/Warning/Error/Critical/None）。
        /// </summary>
        public string ConsoleMinLevel { get; init; } = "Information";
        /// <summary>
        /// 轨道分类日志目录（相对或绝对路径）。
        /// </summary>
        public string CategoryLogDirectory { get; init; } = "logs/looptrack";

        /// <summary>
        /// 是否启用详细状态调试日志（取值：true/false）。
        /// </summary>
        public bool EnableVerboseStatus { get; init; }

        /// <summary>
        /// 是否启用轨道分类文件日志（取值：true/false）。
        /// </summary>
        public bool EnableCategoryFile { get; init; } = true;

        /// <summary>
        /// 分类文件保留天数（最小值：1，建议范围：1~30）。
        /// </summary>
        public int CategoryRetentionDays { get; init; } = 7;

        /// <summary>
        /// 是否启用实时速度日志（取值：true/false）。
        /// </summary>
        public bool EnableRealtimeSpeedLog { get; init; } = true;

        /// <summary>
        /// 是否启用 PID 调参日志（取值：true/false）。
        /// </summary>
        public bool EnablePidTuningLog { get; init; } = true;

        /// <summary>
        /// Info 状态日志输出间隔（单位：ms，最小值：100，建议范围：1000~10000）。
        /// </summary>
        public int InfoStatusIntervalMs { get; init; } = 3000;

        /// <summary>
        /// 调试状态日志输出间隔（单位：ms，最小值：100，建议范围：500~5000）。
        /// </summary>
        public int DebugStatusIntervalMs { get; init; } = 1000;

        /// <summary>
        /// 实时速度日志输出间隔（单位：ms，最小值：100，建议范围：500~5000）。
        /// </summary>
        public int RealtimeSpeedLogIntervalMs { get; init; } = 1000;

        /// <summary>
        /// PID 调参日志输出间隔（单位：ms，最小值：100，建议范围：500~5000）。
        /// </summary>
        public int PidTuningLogIntervalMs { get; init; } = 1000;

        /// <summary>
        /// 失稳偏差阈值（单位：mm/s，最小值：0，建议根据现场稳速精度配置）。
        /// </summary>
        public decimal UnstableDeviationThresholdMmps { get; init; } = 100m;

        /// <summary>
        /// 失稳持续判定时长（单位：ms，最小值：0，建议范围：500~10000）。
        /// </summary>
        public int UnstableDurationMs { get; init; } = 3000;
    }
}
