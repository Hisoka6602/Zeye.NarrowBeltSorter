using Zeye.NarrowBeltSorter.Core.Enums.SiemensS7;

namespace Zeye.NarrowBeltSorter.Core.Options.SiemensS7 {
    /// <summary>
    /// 西门子 S7 厂商配置聚合。
    /// </summary>
    public sealed record SiemensS7Options {

        /// <summary>
        /// 是否启用西门子 S7 厂商配置。
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// EMC 连接参数配置。
        /// </summary>
        public S7EmcConnectionOptions EmcConnection { get; set; } = new();

        /// <summary>
        /// 逻辑点位绑定配置集合。
        /// </summary>
        public List<SiemensS7PointBindingOptions> PointBindings { get; set; } = new();

        /// <summary>
        /// 传感器配置集合。
        /// </summary>
        public List<SiemensS7SensorOptions> Sensors { get; set; } = new();

        /// <summary>
        /// 校验 SiemensS7 相关配置边界与关联约束。
        /// </summary>
        /// <returns>校验错误集合。</returns>
        public IReadOnlyList<string> Validate() {
            var errors = new List<string>();
            errors.AddRange(EmcConnection.Validate());
            var pointIds = new HashSet<int>();
            for (var i = 0; i < PointBindings.Count; i++) {
                var binding = PointBindings[i];
                if (binding.PointId <= 0) {
                    errors.Add($"SiemensS7:PointBindings[{i}]:PointId 必须大于 0，当前值：{binding.PointId}。");
                }

                if (!pointIds.Add(binding.PointId)) {
                    errors.Add($"SiemensS7:PointBindings 点位编号重复，PointId={binding.PointId}。");
                }

                if (string.IsNullOrWhiteSpace(binding.Name)) {
                    errors.Add($"SiemensS7:PointBindings[{i}]:Name 不能为空。");
                }

                if (binding.Area == SiemensS7AddressArea.DataBlock && binding.DbNumber <= 0) {
                    errors.Add($"SiemensS7:PointBindings[{i}]:DbNumber 在 DataBlock 区必须大于 0，当前值：{binding.DbNumber}。");
                }

                if (binding.Area != SiemensS7AddressArea.DataBlock && binding.DbNumber != 0) {
                    errors.Add($"SiemensS7:PointBindings[{i}]:DbNumber 仅 DataBlock 区允许配置，当前值：{binding.DbNumber}。");
                }

                if (binding.ByteOffset < 0) {
                    errors.Add($"SiemensS7:PointBindings[{i}]:ByteOffset 不能小于 0，当前值：{binding.ByteOffset}。");
                }

                if (binding.BitIndex is < 0 or > 7) {
                    errors.Add($"SiemensS7:PointBindings[{i}]:BitIndex 必须在 0~7，当前值：{binding.BitIndex}。");
                }
            }

            for (var i = 0; i < Sensors.Count; i++) {
                var sensor = Sensors[i];
                if (string.IsNullOrWhiteSpace(sensor.Name)) {
                    errors.Add($"SiemensS7:Sensors[{i}]:Name 不能为空。");
                }

                if (sensor.DebounceWindowMs < 0) {
                    errors.Add($"SiemensS7:Sensors[{i}]:DebounceWindowMs 不能小于 0，当前值：{sensor.DebounceWindowMs}。");
                }

                var binding = PointBindings.FirstOrDefault(item => item.PointId == sensor.PointId);
                if (binding is null) {
                    errors.Add($"SiemensS7:Sensors[{i}]:PointId={sensor.PointId} 未在 PointBindings 中定义。");
                    continue;
                }

                if (binding.Area == SiemensS7AddressArea.Output) {
                    errors.Add($"SiemensS7:Sensors[{i}]:PointId={sensor.PointId} 不能映射到输出区。");
                }
            }

            return errors;
        }
    }
}
