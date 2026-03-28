using Zeye.NarrowBeltSorter.Core.Utilities.Chutes;

namespace Zeye.NarrowBeltSorter.Core.Options.Chutes {

    /// <summary>
    /// 智嵌单台继电器设备配置（每台设备独立配置，对应一个物理 32 路继电器板）。
    /// 一个项目可配置多台设备（格口数量超过 32 时），每台设备最多管理 32 路 Y01~Y32。
    /// </summary>
    public sealed record ZhiQianDeviceOptions {

        /// <summary>
        /// 设备 IP 地址（必填）。
        /// </summary>
        public string Host { get; set; } = "192.168.1.253";

        /// <summary>
        /// 设备端口（手册默认 1030，合法范围：1~65535）。
        /// </summary>
        public int Port { get; set; } = 1030;

        /// <summary>
        /// 设备地址（协议站号 0~255，手册默认 1）。
        /// </summary>
        public byte DeviceAddress { get; set; } = 1;

        /// <summary>
        /// 格口绑定关系（键：业务格口 Id；值：Y 路编号 1~32）。
        /// 同一台设备内每个 chuteId 唯一，每条 Y 路唯一且范围 1~32。
        /// 跨设备格口 Id 唯一性由 ZhiQianChuteOptions.Validate() 统一检查。
        /// </summary>
        public Dictionary<long, int> ChuteToDoMap { get; set; } = new();

        /// <summary>
        /// 校验单台设备配置，失败时返回错误描述列表。
        /// </summary>
        /// <param name="deviceIndex">设备在 Devices 列表中的下标（用于错误信息定位）。</param>
        /// <returns>错误描述集合，空表示校验通过。</returns>
        public IReadOnlyList<string> Validate(int deviceIndex) {
            var errors = new List<string>();
            var prefix = $"Devices[{deviceIndex}]";

            // 步骤1：校验 TCP 连接必填字段。
            if (string.IsNullOrWhiteSpace(Host)) {
                errors.Add($"{prefix}.Host 不能为空。");
            }

            if (Port is < 1 or > 65535) {
                errors.Add($"{prefix}.Port 必须在 1~65535 范围，当前值：{Port}。");
            }

            if (DeviceAddress > 255) {
                errors.Add($"{prefix}.DeviceAddress 必须在 0~255 范围，当前值：{DeviceAddress}。");
            }

            // 步骤2：校验 Y 路映射非空，并检查路号范围（1~32）与唯一性。
            if (ChuteToDoMap.Count == 0) {
                errors.Add($"{prefix}.ChuteToDoMap 不能为空，至少需要一条格口绑定关系。");
            }

            var seenDoIndexes = new HashSet<int>();
            foreach (var (chuteId, doIndex) in ChuteToDoMap) {
                if (!ZhiQianAddressMap.IsValidDoIndex(doIndex)) {
                    errors.Add($"{prefix}.ChuteToDoMap 中 chuteId={chuteId} 对应的 Y 路 {doIndex} 不在 1~32 范围。");
                }

                if (!seenDoIndexes.Add(doIndex)) {
                    errors.Add($"{prefix}.ChuteToDoMap 中 Y 路 {doIndex} 重复绑定，每条 Y 路只能对应一个格口。");
                }
            }

            return errors;
        }
    }
}
