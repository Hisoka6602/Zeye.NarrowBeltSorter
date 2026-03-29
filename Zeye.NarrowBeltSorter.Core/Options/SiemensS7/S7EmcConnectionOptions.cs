namespace Zeye.NarrowBeltSorter.Core.Options.SiemensS7 {
    /// <summary>
    /// 西门子 S7 EMC 连接参数配置。
    /// </summary>
    public sealed record S7EmcConnectionOptions {

        /// <summary>
        /// PLC 终端地址（示例：192.168.1.10）。
        /// </summary>
        public string Endpoint { get; set; } = "192.168.1.10";

        /// <summary>
        /// CPU 型号字符串（示例：S71500）。
        /// </summary>
        public string CpuType { get; set; } = "S71500";

        /// <summary>
        /// 机架号。
        /// </summary>
        public int Rack { get; set; }

        /// <summary>
        /// 槽位号。
        /// </summary>
        public int Slot { get; set; } = 1;

        /// <summary>
        /// IO 轮询周期（毫秒）。
        /// </summary>
        public int IoPollIntervalMs { get; set; } = 100;

        /// <summary>
        /// 自动重连最小延迟（毫秒）。
        /// </summary>
        public int ReconnectMinDelayMs { get; set; } = 200;

        /// <summary>
        /// 自动重连最大延迟（毫秒）。
        /// </summary>
        public int ReconnectMaxDelayMs { get; set; } = 5000;

        /// <summary>
        /// 自动重连指数退避因子。
        /// </summary>
        public decimal ReconnectBackoffFactor { get; set; } = 2.0m;

        /// <summary>
        /// 校验连接参数边界。
        /// </summary>
        /// <returns>校验错误集合。</returns>
        public IReadOnlyList<string> Validate() {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(Endpoint)) {
                errors.Add("SiemensS7:EmcConnection:Endpoint 不能为空。");
            }

            if (string.IsNullOrWhiteSpace(CpuType)) {
                errors.Add("SiemensS7:EmcConnection:CpuType 不能为空。");
            }

            if (Rack < 0) {
                errors.Add($"SiemensS7:EmcConnection:Rack 不能小于 0，当前值：{Rack}。");
            }

            if (Slot < 0) {
                errors.Add($"SiemensS7:EmcConnection:Slot 不能小于 0，当前值：{Slot}。");
            }

            if (IoPollIntervalMs < 20) {
                errors.Add($"SiemensS7:EmcConnection:IoPollIntervalMs 最小值为 20，当前值：{IoPollIntervalMs}。");
            }

            if (ReconnectMinDelayMs < 20) {
                errors.Add($"SiemensS7:EmcConnection:ReconnectMinDelayMs 最小值为 20，当前值：{ReconnectMinDelayMs}。");
            }

            if (ReconnectMaxDelayMs < ReconnectMinDelayMs) {
                errors.Add($"SiemensS7:EmcConnection:ReconnectMaxDelayMs 不能小于 ReconnectMinDelayMs，当前值：{ReconnectMaxDelayMs}。");
            }

            if (ReconnectBackoffFactor < 1.0m) {
                errors.Add($"SiemensS7:EmcConnection:ReconnectBackoffFactor 最小值为 1.0，当前值：{ReconnectBackoffFactor}。");
            }

            return errors;
        }
    }
}
