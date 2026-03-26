namespace Zeye.NarrowBeltSorter.Core.Options.LoopTrack {
    /// <summary>
    /// 环形轨道日志配置。
    /// </summary>
    public sealed record LoopTrackLoggingOptions {
        /// <summary>
        /// 轨道分类日志目录（相对或绝对路径）。
        /// </summary>
        public string CategoryLogDirectory { get; init; } = "logs/looptrack";

        /// <summary>
        /// 是否启用详细状态调试日志。
        /// </summary>
        public bool EnableVerboseStatus { get; init; }

        /// <summary>
        /// looptrack-status 类别是否启用。
        /// </summary>
        public bool EnableStatusCategoryFile { get; init; } = true;

        /// <summary>
        /// looptrack-status 最低级别。
        /// </summary>
        public string StatusCategoryMinLevel { get; init; } = "Information";

        /// <summary>
        /// looptrack-status 文件保留天数。
        /// </summary>
        public int StatusCategoryRetentionDays { get; init; } = 7;

        /// <summary>
        /// looptrack-pid 类别是否启用。
        /// </summary>
        public bool EnablePidCategoryFile { get; init; } = true;

        /// <summary>
        /// looptrack-pid 最低级别。
        /// </summary>
        public string PidCategoryMinLevel { get; init; } = "Debug";

        /// <summary>
        /// looptrack-pid 文件保留天数。
        /// </summary>
        public int PidCategoryRetentionDays { get; init; } = 7;

        /// <summary>
        /// looptrack-modbus 类别是否启用。
        /// </summary>
        public bool EnableModbusCategoryFile { get; init; } = true;

        /// <summary>
        /// looptrack-modbus 最低级别。
        /// </summary>
        public string ModbusCategoryMinLevel { get; init; } = "Information";

        /// <summary>
        /// looptrack-modbus 文件保留天数。
        /// </summary>
        public int ModbusCategoryRetentionDays { get; init; } = 7;

        /// <summary>
        /// looptrack-fault 类别是否启用。
        /// </summary>
        public bool EnableFaultCategoryFile { get; init; } = true;

        /// <summary>
        /// looptrack-fault 最低级别。
        /// </summary>
        public string FaultCategoryMinLevel { get; init; } = "Warning";

        /// <summary>
        /// looptrack-fault 文件保留天数。
        /// </summary>
        public int FaultCategoryRetentionDays { get; init; } = 14;

        /// <summary>
        /// 是否启用实时速度日志。
        /// </summary>
        public bool EnableRealtimeSpeedLog { get; init; } = true;

        /// <summary>
        /// 是否启用 PID 调参日志。
        /// </summary>
        public bool EnablePidTuningLog { get; init; } = true;

        /// <summary>
        /// Info 状态日志输出间隔（毫秒）。
        /// </summary>
        public int InfoStatusIntervalMs { get; init; } = 3000;

        /// <summary>
        /// 调试状态日志输出间隔（毫秒）。
        /// </summary>
        public int DebugStatusIntervalMs { get; init; } = 1000;

        /// <summary>
        /// 实时速度日志输出间隔（毫秒）。
        /// </summary>
        public int RealtimeSpeedLogIntervalMs { get; init; } = 1000;

        /// <summary>
        /// PID 调参日志输出间隔（毫秒）。
        /// </summary>
        public int PidTuningLogIntervalMs { get; init; } = 1000;

        /// <summary>
        /// 失稳偏差阈值（mm/s）。
        /// </summary>
        public decimal UnstableDeviationThresholdMmps { get; init; } = 100m;

        /// <summary>
        /// 失稳持续判定时长（毫秒）。
        /// </summary>
        public int UnstableDurationMs { get; init; } = 3000;
    }
}
