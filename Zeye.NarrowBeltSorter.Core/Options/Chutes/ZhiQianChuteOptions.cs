using Zeye.NarrowBeltSorter.Core.Enums.Chutes;

namespace Zeye.NarrowBeltSorter.Core.Options.Chutes {

    /// <summary>
    /// 智嵌格口管理器共享配置（对应 appsettings Chutes:ZhiQian 节点）。
    /// 支持多台继电器设备（每台 Devices[i] 独立配置 Host/Port/ChuteToDoMap），
    /// 业务格口可跨设备，但强排口全局唯一（由 ZhiQianCompositeChuteManager 约束）。
    /// </summary>
    public sealed record ZhiQianChuteOptions {

        /// <summary>
        /// 是否启用智嵌格口驱动（false 时不注册该驱动）。
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 继电器设备列表（至少一台）。每台设备管理最多 32 路继电器（Y01~Y32）。
        /// 每台设备对应一个独立 TCP 连接通道，通道内指令严格串行（不支持并行指令）。
        /// </summary>
        public List<ZhiQianDeviceOptions> Devices { get; set; } = new();

        /// <summary>
        /// 单次命令超时（单位：毫秒，最小值 100）。
        /// </summary>
        public int CommandTimeoutMs { get; set; } = 300;

        /// <summary>
        /// 重试次数（不含首次，建议 0~5）。
        /// </summary>
        public int RetryCount { get; set; } = 2;

        /// <summary>
        /// 重试间隔（单位：毫秒，最小值 10）。
        /// </summary>
        public int RetryDelayMs { get; set; } = 50;

        /// <summary>
        /// 状态轮询周期（单位：毫秒，最小值 50）。
        /// </summary>
        public int PollIntervalMs { get; set; } = 100;

        /// <summary>
        /// 是否启用写后读校验（写 DO 后立即回读并比对，建议默认 true）。
        /// </summary>
        public bool EnableWriteBackVerify { get; set; } = true;

        /// <summary>
        /// 写后读校验策略（WarnOnly：仅告警；RetryThenFail：失败后重试一次仍失败则返回 false 并置故障）。
        /// </summary>
        public WriteVerifyMode WriteVerifyMode { get; set; } = WriteVerifyMode.WarnOnly;

        /// <summary>
        /// 默认开闸持续时长（单位：毫秒，无明确时窗时使用，最小值 20）。
        /// </summary>
        public int DefaultOpenDurationMs { get; set; } = 120;

        /// <summary>
        /// 强排是否独占（true 时关闭其他路，仅目标路打开；默认 true 更安全）。
        /// </summary>
        public bool ForceOpenExclusive { get; set; } = true;

        /// <summary>
        /// 格口日志配置（控制状态、通信与异常日志落盘目录与开关）。
        /// </summary>
        public ZhiQianLoggingOptions Logging { get; set; } = new();

        /// <summary>
        /// 校验配置合法性，失败时返回错误描述列表。
        /// </summary>
        /// <returns>错误描述集合，空表示校验通过。</returns>
        public IReadOnlyList<string> Validate() {
            var errors = new List<string>();
            // 步骤1：校验共享通信参数边界。
            if (CommandTimeoutMs < 100) {
                errors.Add($"CommandTimeoutMs 最小值为 100，当前值：{CommandTimeoutMs}。");
            }

            if (RetryCount < 0) {
                errors.Add($"RetryCount 不能小于 0，当前值：{RetryCount}。");
            }

            if (RetryDelayMs < 10) {
                errors.Add($"RetryDelayMs 最小值为 10，当前值：{RetryDelayMs}。");
            }

            if (PollIntervalMs < 50) {
                errors.Add($"PollIntervalMs 最小值为 50，当前值：{PollIntervalMs}。");
            }

            if (DefaultOpenDurationMs < 20) {
                errors.Add($"DefaultOpenDurationMs 最小值为 20，当前值：{DefaultOpenDurationMs}。");
            }

            // 步骤2：校验设备列表非空，并逐台校验；同时检查跨设备格口 Id 唯一性。
            if (Devices.Count == 0) {
                errors.Add("Devices 不能为空，至少需要配置一台继电器设备。");
            }

            var seenChuteIds = new HashSet<long>();
            for (var i = 0; i < Devices.Count; i++) {
                foreach (var err in Devices[i].Validate(i)) {
                    errors.Add(err);
                }

                foreach (var chuteId in Devices[i].ChuteToDoMap.Keys) {
                    if (!seenChuteIds.Add(chuteId)) {
                        errors.Add($"格口 chuteId={chuteId} 在多台设备中重复绑定，每个格口只能对应一台设备。");
                    }
                }
            }

            return errors;
        }
    }
}
