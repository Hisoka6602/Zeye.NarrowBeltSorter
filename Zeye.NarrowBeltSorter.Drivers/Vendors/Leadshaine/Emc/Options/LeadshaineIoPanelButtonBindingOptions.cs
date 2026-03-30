using Zeye.NarrowBeltSorter.Core.Enums.Io;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc.Options {
    /// <summary>
    /// Leadshaine IoPanel 按钮点位绑定配置。
    /// </summary>
    public sealed record LeadshaineIoPanelButtonBindingOptions : LeadshainePointReferenceOptions {
        /// <summary>
        /// 按钮名称。
        /// </summary>
        public string ButtonName { get; set; } = string.Empty;

        /// <summary>
        /// 按钮角色类型。
        /// </summary>
        public IoPanelButtonType ButtonType { get; set; } = IoPanelButtonType.Unspecified;
    }
}
