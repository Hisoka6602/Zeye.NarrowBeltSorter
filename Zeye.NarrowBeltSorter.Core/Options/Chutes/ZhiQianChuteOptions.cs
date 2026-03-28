using Zeye.NarrowBeltSorter.Core.Enums.Chutes;
using Zeye.NarrowBeltSorter.Core.Utilities.Chutes;

namespace Zeye.NarrowBeltSorter.Core.Options.Chutes {

    /// <summary>
    /// 智嵌 32 路网络继电器格口管理器配置（对应 appsettings Chutes:ZhiQian 节点）。
    /// 通信协议固定为 ASCII TCP（手册第 7.2 节），无需 Modbus 协议层。
    /// </summary>
    public sealed record ZhiQianChuteOptions {

        /// <summary>
        /// 是否启用智嵌格口驱动（false 时不注册该驱动）。
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 设备 IP 地址（必填）。
        /// </summary>
        public string Host { get; set; } = "192.168.1.253";

        /// <summary>
        /// 设备端口（手册默认端口 1030，合法范围：1~65535）。
        /// </summary>
        public int Port { get; set; } = 1030;

        /// <summary>
        /// 设备地址（ASCII 协议站号 0~255，手册默认 1）。
        /// </summary>
        public byte DeviceAddress { get; set; } = 1;

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
        /// 格口绑定关系（键：业务格口 Id；值：Y 路编号 1~32）。
        /// 每个 chuteId 唯一，每个 Y 路唯一且范围 1~32。
        /// </summary>
        public Dictionary<long, int> ChuteToDoMap { get; set; } = new();

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
            // 步骤1：校验 TCP 连接必填字段。
            if (string.IsNullOrWhiteSpace(Host)) {
                errors.Add("Host 不能为空。");
            }

            if (Port is < 1 or > 65535) {
                errors.Add($"Port 必须在 1~65535 范围，当前值：{Port}。");
            }

            // 步骤2：校验设备地址与通信参数边界。
            if (DeviceAddress > 255) {
                errors.Add($"DeviceAddress 必须在 0~255 范围（ASCII 协议站号），当前值：{DeviceAddress}。");
            }

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

            // 步骤3：校验 Y 路映射非空，并检查路号范围（1~32）与唯一性（不可重复绑定）。
            if (ChuteToDoMap.Count == 0) {
                errors.Add("ChuteToDoMap 不能为空，至少需要一条格口绑定关系。");
            }

            var seenDoIndexes = new HashSet<int>();
            foreach (var (chuteId, doIndex) in ChuteToDoMap) {
                if (!ZhiQianAddressMap.IsValidDoIndex(doIndex)) {
                    errors.Add($"ChuteToDoMap 中 chuteId={chuteId} 对应的 Y 路 {doIndex} 不在 1~32 范围。");
                }

                if (!seenDoIndexes.Add(doIndex)) {
                    errors.Add($"ChuteToDoMap 中 Y 路 {doIndex} 重复绑定，每条 Y 路只能对应一个格口。");
                }
            }

            return errors;
        }
    }
}
