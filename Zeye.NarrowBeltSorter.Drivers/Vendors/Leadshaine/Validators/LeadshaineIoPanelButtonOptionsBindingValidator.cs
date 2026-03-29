using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Validators {
    /// <summary>
    /// Leadshaine IoPanel 按钮绑定校验器。
    /// </summary>
    public sealed class LeadshaineIoPanelButtonOptionsBindingValidator {
        /// <summary>
        /// 校验 IoPanel 按钮绑定是否引用有效点位。
        /// </summary>
        /// <param name="buttonOptions">按钮绑定集合。</param>
        /// <param name="pointBindingOptions">点位绑定集合。</param>
        /// <returns>配置错误集合。</returns>
        public IReadOnlyList<string> Validate(
            LeadshaineIoPanelButtonBindingCollectionOptions buttonOptions,
            LeadshainePointBindingCollectionOptions pointBindingOptions) {
            var validationErrors = new List<string>(4);
            var validPointIds = pointBindingOptions.Points
                .Select(x => x.PointId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < buttonOptions.Buttons.Count; i++) {
                var button = buttonOptions.Buttons[i];
                var buttonPath = $"Leadshaine.IoPanel.Buttons[{i}]";
                if (string.IsNullOrWhiteSpace(button.ButtonName)) {
                    validationErrors.Add($"{buttonPath}.ButtonName 不能为空。");
                }

                if (string.IsNullOrWhiteSpace(button.PointId)) {
                    validationErrors.Add($"{buttonPath}.PointId 不能为空。");
                    continue;
                }

                if (!validPointIds.Contains(button.PointId)) {
                    validationErrors.Add($"{buttonPath}.PointId={button.PointId} 未在 Leadshaine.PointBindings.Points 中定义。");
                }
            }

            return validationErrors;
        }
    }
}
