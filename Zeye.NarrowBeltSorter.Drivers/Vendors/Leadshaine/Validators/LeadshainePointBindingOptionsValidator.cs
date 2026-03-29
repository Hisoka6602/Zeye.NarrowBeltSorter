using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Validators {
    /// <summary>
    /// Leadshaine 点位绑定配置校验器。
    /// </summary>
    public sealed class LeadshainePointBindingOptionsValidator {
        /// <summary>
        /// 最大端口号：MaxPortNo = (MaxBitNo + 1) / 32 - 1 = (65535 + 1) / 32 - 1 = 2047。
        /// </summary>
        private const int MaxPortNo = 2047;

        /// <summary>
        /// 最大位号：底层 WriteOutBit 使用 ushort，最大值为 65535。
        /// </summary>
        private const int MaxBitNo = 65535;

        /// <summary>
        /// 校验点位绑定集合配置。
        /// </summary>
        /// <param name="options">点位绑定集合配置。</param>
        /// <returns>配置错误集合。</returns>
        public IReadOnlyList<string> Validate(LeadshainePointBindingCollectionOptions options) {
            var validationErrors = new List<string>();
            var points = options.Points;

            // 步骤1：校验 PointId 全局唯一，防止逻辑点位覆盖。
            var seenPointIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < points.Count; i++) {
                var point = points[i];
                var pointPath = $"Leadshaine.PointBindings.Points[{i}]";
                if (string.IsNullOrWhiteSpace(point.PointId)) {
                    validationErrors.Add($"{pointPath}.PointId 不能为空。");
                }
                else if (!seenPointIds.Add(point.PointId)) {
                    validationErrors.Add($"{pointPath}.PointId={point.PointId} 重复，PointId 必须唯一。");
                }

                ValidateBinding(point.Binding, pointPath, validationErrors);
            }

            return validationErrors;
        }

        /// <summary>
        /// 校验物理位绑定配置。
        /// </summary>
        /// <param name="binding">物理位绑定配置。</param>
        /// <param name="pointPath">点位路径。</param>
        /// <param name="errors">错误集合。</param>
        private static void ValidateBinding(LeadshaineBitBindingOptions binding, string pointPath, List<string> errors) {
            var area = binding.Area?.Trim();
            if (!string.Equals(area, "Input", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(area, "Output", StringComparison.OrdinalIgnoreCase)) {
                errors.Add($"{pointPath}.Binding.Area 仅支持 Input 或 Output，当前值：{binding.Area}。");
            }

            if (binding.BitIndex < 0 || binding.BitIndex > 31) {
                errors.Add($"{pointPath}.Binding.BitIndex 必须在 0~31 之间，当前值：{binding.BitIndex}。");
            }

            if (binding.PortNo > MaxPortNo) {
                errors.Add($"{pointPath}.Binding.PortNo 过大，当前值：{binding.PortNo}，最大允许值：{MaxPortNo}。");
            }

            var bitNo = binding.PortNo * 32 + binding.BitIndex;
            if (bitNo > MaxBitNo) {
                errors.Add($"{pointPath}.Binding.PortNo 与 BitIndex 组合超出 16 位范围，计算值：{bitNo}，最大允许值：{MaxBitNo}。");
            }

            var triggerState = binding.TriggerState?.Trim();
            if (!string.Equals(triggerState, "High", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(triggerState, "Low", StringComparison.OrdinalIgnoreCase)) {
                errors.Add($"{pointPath}.Binding.TriggerState 仅支持 High 或 Low，当前值：{binding.TriggerState}。");
            }
        }
    }
}
