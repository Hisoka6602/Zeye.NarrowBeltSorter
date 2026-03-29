using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Validators {
    /// <summary>
    /// Leadshaine 点位绑定配置校验器。
    /// </summary>
    public sealed class LeadshainePointBindingOptionsValidator {
        /// <summary>
        /// 校验点位绑定集合配置。
        /// </summary>
        /// <param name="options">点位绑定集合配置。</param>
        /// <returns>配置错误集合。</returns>
        public IReadOnlyList<string> Validate(LeadshainePointBindingCollectionOptions options) {
            var errors = new List<string>();
            var points = options.Points;

            // 步骤1：校验集合非空，避免后续业务在空点位场景中运行。
            if (points.Count == 0) {
                errors.Add("Leadshaine.PointBindings.Points 不能为空，至少需要配置一个点位。");
                return errors;
            }

            // 步骤2：校验 PointId 全局唯一，防止逻辑点位覆盖。
            var seenPointIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < points.Count; i++) {
                var point = points[i];
                var pointPath = $"Leadshaine.PointBindings.Points[{i}]";
                if (string.IsNullOrWhiteSpace(point.PointId)) {
                    errors.Add($"{pointPath}.PointId 不能为空。");
                }
                else if (!seenPointIds.Add(point.PointId)) {
                    errors.Add($"{pointPath}.PointId={point.PointId} 重复，PointId 必须唯一。");
                }

                ValidateBinding(point.Binding, pointPath, errors);
            }

            return errors;
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

            var triggerState = binding.TriggerState?.Trim();
            if (!string.Equals(triggerState, "High", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(triggerState, "Low", StringComparison.OrdinalIgnoreCase)) {
                errors.Add($"{pointPath}.Binding.TriggerState 仅支持 High 或 Low，当前值：{binding.TriggerState}。");
            }
        }
    }
}
