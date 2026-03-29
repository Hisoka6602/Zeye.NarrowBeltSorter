namespace Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine {
    /// <summary>
    /// Leadshaine EMC 连接参数配置。
    /// </summary>
    public sealed record LeadshaineEmcConnectionOptions {
        /// <summary>
        /// 是否启用 Leadshaine EMC 配置。
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 控制器连接超时时间（毫秒）。
        /// </summary>
        public int ConnectionTimeoutMs { get; set; } = 3000;

        /// <summary>
        /// 控制卡编号。
        /// </summary>
        public ushort CardNo { get; set; }

        /// <summary>
        /// 错误码读取通道编号。
        /// </summary>
        public ushort Channel { get; set; }

        /// <summary>
        /// 控制器 IP；为 null 或空字符串时使用本地板卡模式初始化。
        /// </summary>
        public string? ControllerIp { get; set; }

        /// <summary>
        /// 初始化最大重试次数（不含首次尝试）。
        /// </summary>
        public int InitializeRetryCount { get; set; } = 3;

        /// <summary>
        /// 初始化重试初始间隔（毫秒）。
        /// </summary>
        public int InitializeRetryDelayMs { get; set; } = 300;

        /// <summary>
        /// IO 轮询间隔（毫秒）。
        /// </summary>
        public int PollingIntervalMs { get; set; } = 100;

        /// <summary>
        /// 断链重连初始间隔（毫秒）。
        /// </summary>
        public int ReconnectBaseDelayMs { get; set; } = 200;

        /// <summary>
        /// 断链重连最大间隔（毫秒）。
        /// </summary>
        public int ReconnectMaxDelayMs { get; set; } = 5000;

        /// <summary>
        /// 校验配置边界是否合法。
        /// </summary>
        /// <returns>配置错误集合。</returns>
        public IReadOnlyList<string> Validate() {
            var validationErrors = new List<string>(9);

            // 步骤1：校验基础时间参数边界, 避免出现无效或负数配置。
            if (ConnectionTimeoutMs <= 0) {
                validationErrors.Add($"ConnectionTimeoutMs 必须大于 0，当前值：{ConnectionTimeoutMs}。");
            }

            if (InitializeRetryCount < 0) {
                validationErrors.Add($"InitializeRetryCount 不能小于 0，当前值：{InitializeRetryCount}。");
            }

            if (InitializeRetryDelayMs <= 0) {
                validationErrors.Add($"InitializeRetryDelayMs 必须大于 0，当前值：{InitializeRetryDelayMs}。");
            }

            if (PollingIntervalMs <= 0) {
                validationErrors.Add($"PollingIntervalMs 必须大于 0，当前值：{PollingIntervalMs}。");
            }

            // 步骤2：校验重连参数关系，确保退避区间有效。
            if (ReconnectBaseDelayMs <= 0) {
                validationErrors.Add($"ReconnectBaseDelayMs 必须大于 0，当前值：{ReconnectBaseDelayMs}。");
            }

            if (ReconnectMaxDelayMs <= 0) {
                validationErrors.Add($"ReconnectMaxDelayMs 必须大于 0，当前值：{ReconnectMaxDelayMs}。");
            }

            if (ReconnectMaxDelayMs < ReconnectBaseDelayMs) {
                validationErrors.Add($"ReconnectMaxDelayMs 必须大于或等于 ReconnectBaseDelayMs，当前值：{ReconnectMaxDelayMs} < {ReconnectBaseDelayMs}。");
            }

            if (!string.IsNullOrWhiteSpace(ControllerIp) && !System.Net.IPAddress.TryParse(ControllerIp, out _)) {
                validationErrors.Add($"ControllerIp 必须为合法 IP 地址或空字符串，当前值：{ControllerIp}。");
            }

            return validationErrors;
        }
    }
}
