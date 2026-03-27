using System.IO.Ports;
using Zeye.NarrowBeltSorter.Core.Enums.Chutes;
using Zeye.NarrowBeltSorter.Core.Utilities.Chutes;

namespace Zeye.NarrowBeltSorter.Core.Options.Chutes {

    /// <summary>
    /// 智嵌 32 路网络继电器格口管理器配置（对应 appsettings Chutes:ZhiQian 节点）。
    /// </summary>
    public sealed record ZhiQianChuteOptions {

        /// <summary>
        /// 是否启用智嵌格口驱动（false 时不注册该驱动）。
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 通信传输模式（ModbusTcp / ModbusRtu）。
        /// </summary>
        public ZhiQianTransport Transport { get; set; } = ZhiQianTransport.ModbusTcp;

        /// <summary>
        /// 设备 IP 地址（Transport=ModbusTcp 时必填）。
        /// </summary>
        public string Host { get; set; } = "192.168.1.253";

        /// <summary>
        /// 设备端口（Transport=ModbusTcp 时必填，合法范围：1~65535）。
        /// </summary>
        public int Port { get; set; } = 1030;

        /// <summary>
        /// 串口名称（Transport=ModbusRtu 时必填）。
        /// </summary>
        public string SerialPortName { get; set; } = string.Empty;

        /// <summary>
        /// 串口波特率（Transport=ModbusRtu 时必填，手册默认 115200）。
        /// </summary>
        public int BaudRate { get; set; } = 115200;

        /// <summary>
        /// 串口数据位（Transport=ModbusRtu 时使用，手册默认 8）。
        /// </summary>
        public int DataBits { get; set; } = 8;

        /// <summary>
        /// 串口校验位名称（None/Odd/Even，Transport=ModbusRtu 时使用，手册默认 None）。
        /// </summary>
        public string Parity { get; set; } = "None";

        /// <summary>
        /// 串口停止位名称（One/Two，Transport=ModbusRtu 时使用，手册默认 One）。
        /// </summary>
        public string StopBits { get; set; } = "One";

        /// <summary>
        /// 设备站号（Modbus 从站地址，手册默认 1，按协议范围校验）。
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
            // 步骤1：校验传输层必填字段（ModbusTcp 时 Host/Port；ModbusRtu 时 SerialPortName）。
            if (Transport == ZhiQianTransport.ModbusTcp) {
                if (string.IsNullOrWhiteSpace(Host)) {
                    errors.Add("Transport=ModbusTcp 时 Host 不能为空。");
                }

                if (Port is < 1 or > 65535) {
                    errors.Add($"Port 必须在 1~65535 范围，当前值：{Port}。");
                }
            }
            else {
                if (string.IsNullOrWhiteSpace(SerialPortName)) {
                    errors.Add("Transport=ModbusRtu 时 SerialPortName 不能为空。");
                }

                if (!Enum.TryParse<Parity>(Parity, ignoreCase: true, out _)) {
                    errors.Add($"Parity 值非法：{Parity}，合法值：None/Odd/Even/Mark/Space。");
                }

                if (!Enum.TryParse<StopBits>(StopBits, ignoreCase: true, out _)) {
                    errors.Add($"StopBits 值非法：{StopBits}，合法值：None/One/OnePointFive/Two。");
                }
            }

            // 步骤2：校验设备站号与通信参数边界。
            if (DeviceAddress is 0 or > 247) {
                errors.Add($"DeviceAddress 必须在 1~247 范围，当前值：{DeviceAddress}。");
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

            // 步骤3：校验 Y路映射非空，并检查路号范围（1~32）与唯一性（不可重复绑定）。
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
