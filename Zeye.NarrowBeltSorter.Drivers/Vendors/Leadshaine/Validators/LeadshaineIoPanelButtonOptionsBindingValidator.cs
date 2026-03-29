using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc.Options;

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
            var validPointIds = pointBindingOptions.Points
                .Select(x => x.PointId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return PointBindingReferenceValidator.Validate(
                buttonOptions.Buttons,
                validPointIds,
                static x => x.ButtonName,
                static x => x.PointId,
                static i => $"Leadshaine.IoPanel.Buttons[{i}]",
                "ButtonName",
                "Leadshaine.PointBindings.Points");
        }
    }
}
