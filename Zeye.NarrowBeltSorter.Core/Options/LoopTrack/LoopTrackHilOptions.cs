namespace Zeye.NarrowBeltSorter.Core.Options.LoopTrack {
    /// <summary>
    /// 环轨上机联调（HIL）后台服务配置。
    /// </summary>
    public sealed record LoopTrackHilOptions {
        /// <summary>
        /// 是否启用上机联调后台服务。
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 状态周期日志间隔（毫秒）。
        /// </summary>
        public int StatusLogIntervalMs { get; set; } = 1000;

        /// <summary>
        /// 是否启用按键停轨能力。
        /// </summary>
        public bool EnableKeyboardStop { get; set; } = false;

        /// <summary>
        /// 键盘停轨轮询周期（毫秒）。
        /// </summary>
        public int KeyboardStopPollingIntervalMs { get; set; } = 200;

        /// <summary>
        /// 键盘停轨按键（ConsoleKey 名称）。
        /// </summary>
        public string StopKey { get; set; } = "S";

        /// <summary>
        /// 启动后是否自动连接。
        /// </summary>
        public bool AutoConnectOnStart { get; set; } = true;

        /// <summary>
        /// 连接成功后是否自动清除报警。
        /// </summary>
        public bool AutoClearAlarmAfterConnect { get; set; } = false;

        /// <summary>
        /// 连接成功后是否自动设置主配置中的目标速度（LoopTrack.TargetSpeedMmps）。
        /// </summary>
        public bool AutoSetInitialTargetAfterConnect { get; set; } = false;

        /// <summary>
        /// 连接成功后是否自动启动。
        /// </summary>
        public bool AutoStartAfterConnect { get; set; } = false;

        /// <summary>
        /// 连接失败时最大重试次数（不含首次）。
        /// </summary>
        public int ConnectMaxAttempts { get; set; } = 3;

        /// <summary>
        /// 连接失败重试间隔（毫秒）。
        /// </summary>
        public int ConnectRetryDelayMs { get; set; } = 1000;

        /// <summary>
        /// 是否输出详细事件日志。
        /// </summary>
        public bool EnableVerboseEventLog { get; set; } = true;
    }
}
