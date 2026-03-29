namespace Zeye.NarrowBeltSorter.Core.Options.Leadshaine {
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
        /// 初始化最大重试次数（不含首次）。
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
            var validationErrors = new List<string>(8);

            // 步骤1：校验基础时间参数边界，避免出现无效或负数配置。
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

            return validationErrors;
        }
    }
}
