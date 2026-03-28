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

        public IReadOnlyList<string> Validate(int deviceIndex) {
            var errors = new List<string>();
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

            var seenDoIndexes = new HashSet<int>();
            foreach (var (chuteId, doIndex) in ChuteToDoMap) {
                if (!ZhiQianAddressMap.ValidateDoIndex(doIndex)) {
                    errors.Add($"Devices[{deviceIndex}].ChuteToDoMap 中 chuteId={chuteId} 的 Y 路 {doIndex} 不在 1~32 范围。");
                }

                if (!seenDoIndexes.Add(doIndex)) {
                    errors.Add($"Devices[{deviceIndex}] 中 Y 路 {doIndex} 重复绑定。");
                }
            }

            return errors;
        }
    }
}
