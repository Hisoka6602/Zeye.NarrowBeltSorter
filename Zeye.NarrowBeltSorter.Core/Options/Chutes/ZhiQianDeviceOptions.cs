using Zeye.NarrowBeltSorter.Core.Utilities.Chutes;

namespace Zeye.NarrowBeltSorter.Core.Options.Chutes {
    /// <summary>
    /// 智嵌单台设备配置。
    /// </summary>
    public sealed record ZhiQianDeviceOptions {
        public string Host { get; set; } = "192.168.1.253";

        public int Port { get; set; } = 1030;

        public byte DeviceAddress { get; set; } = 1;

        public Dictionary<long, int> ChuteToDoMap { get; set; } = new();
        /// <summary>
        /// 每个格口对应的红外配置（键：chuteId）。
        /// </summary>
        public Dictionary<long, InfraredChuteOptions> InfraredChuteOptionsMap { get; set; } = new();
        /// <summary>
        /// 校验单台设备配置的字段约束与映射完整性。
        /// </summary>
        /// <param name="deviceIndex">设备序号。</param>
        /// <returns>校验错误集合。</returns>
        public IReadOnlyList<string> Validate(int deviceIndex) {
            var errors = new List<string>();
            // 步骤1：校验主机、端口、站号等基础连接参数边界。
            if (string.IsNullOrWhiteSpace(Host)) {
                errors.Add($"Devices[{deviceIndex}].Host 不能为空。");
            }

            if (Port is < 1 or > 65535) {
                errors.Add($"Devices[{deviceIndex}].Port 必须在 1~65535 范围，当前值：{Port}。");
            }

            if (DeviceAddress is 0 or > 247) {
                errors.Add($"Devices[{deviceIndex}].DeviceAddress 必须在 1~247 范围，当前值：{DeviceAddress}。");
            }

            if (ChuteToDoMap.Count == 0) {
                errors.Add($"Devices[{deviceIndex}].ChuteToDoMap 不能为空。");
            }
            if (InfraredChuteOptionsMap.Count == 0) {
                errors.Add($"Devices[{deviceIndex}].InfraredChuteOptionsMap 不能为空，且需与 ChuteToDoMap 一一对应。");
            }

            // 步骤2：逐条校验 chute->DO 映射有效性、DO 唯一性与红外配置完整性。
            var seenDoIndexes = new HashSet<int>();
            foreach (var (chuteId, doIndex) in ChuteToDoMap) {
                if (!ZhiQianAddressMap.ValidateDoIndex(doIndex)) {
                    errors.Add($"Devices[{deviceIndex}].ChuteToDoMap 中 chuteId={chuteId} 的 Y 路 {doIndex} 不在 1~32 范围。");
                }

                if (!seenDoIndexes.Add(doIndex)) {
                    errors.Add($"Devices[{deviceIndex}] 中 Y 路 {doIndex} 重复绑定。");
                }
                if (!InfraredChuteOptionsMap.ContainsKey(chuteId)) {
                    errors.Add($"Devices[{deviceIndex}].InfraredChuteOptionsMap 缺少 chuteId={chuteId} 的配置。");
                }
            }

            // 步骤3：反向校验红外配置中不存在孤立 chuteId，保证双向一一对应。
            foreach (var chuteId in InfraredChuteOptionsMap.Keys) {
                if (!ChuteToDoMap.ContainsKey(chuteId)) {
                    errors.Add($"Devices[{deviceIndex}].InfraredChuteOptionsMap 中 chuteId={chuteId} 未在 ChuteToDoMap 中定义。");
                }
            }

            return errors;
        }
    }
}
