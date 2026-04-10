namespace Zeye.NarrowBeltSorter.Core.Options.LoopTrack {
    /// <summary>
    /// 环轨上机联调（HIL）后台服务配置。
    /// </summary>
    public sealed record LoopTrackHilOptions {
        /// <summary>
        /// 是否启用上机联调后台服务（取值：true/false）。
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 状态周期日志间隔（单位：ms，最小值：100，建议范围：500~10000）。
        /// </summary>
        public int StatusLogIntervalMs { get; set; } = 1000;

        /// <summary>
        /// 是否启用按键停轨能力（取值：true/false）。
        /// </summary>
        public bool EnableKeyboardStop { get; set; } = false;

        /// <summary>
        /// 键盘停轨轮询周期（单位：ms，最小值：50，建议范围：100~1000）。
        /// </summary>
        public int KeyboardStopPollingIntervalMs { get; set; } = 200;

        /// <summary>
        /// 键盘停轨按键（ConsoleKey 名称）。
        /// </summary>
        public string StopKey { get; set; } = "S";

        /// <summary>
        /// 启动后是否自动连接（取值：true/false）。
        /// </summary>
        public bool AutoConnectOnStart { get; set; } = true;

        /// <summary>
        /// 连接成功后是否自动清除报警（取值：true/false）。
        /// </summary>
        public bool AutoClearAlarmAfterConnect { get; set; } = false;

        /// <summary>
        /// 连接成功后是否自动设置主配置中的目标速度（LoopTrack.TargetSpeedMmps）（取值：true/false）。
        /// </summary>
        public bool AutoSetInitialTargetAfterConnect { get; set; } = false;

        /// <summary>
        /// 连接成功后是否自动启动（取值：true/false）。
        /// </summary>
        public bool AutoStartAfterConnect { get; set; } = false;

        /// <summary>
        /// 连接失败时最大重试次数（不含首次，最小值：0，建议范围：1~10）。
        /// </summary>
        public int ConnectMaxAttempts { get; set; } = 3;

        /// <summary>
        /// 连接失败重试间隔（单位：ms，最小值：100，建议范围：500~5000）。
        /// </summary>
        public int ConnectRetryDelayMs { get; set; } = 1000;

        /// <summary>
        /// 是否输出详细事件日志（取值：true/false）。
        /// </summary>
        public bool EnableVerboseEventLog { get; set; } = true;
    }
}
