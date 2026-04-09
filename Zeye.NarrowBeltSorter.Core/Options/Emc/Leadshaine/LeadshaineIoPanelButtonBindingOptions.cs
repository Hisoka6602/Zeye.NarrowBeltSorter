using Zeye.NarrowBeltSorter.Core.Enums.Io;

namespace Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine {
    /// <summary>
    /// Leadshaine IoPanel 按钮点位绑定配置。
    /// </summary>
    public sealed record LeadshaineIoPanelButtonBindingOptions : LeadshainePointReferenceOptions {
        /// <summary>
        /// 按钮名称。
        /// </summary>
        public string ButtonName { get; set; } = string.Empty;

        /// <summary>
        /// 按钮角色类型（可选值：Unspecified/Start/Stop/EmergencyStop/Reset/MaintenanceSwitch）。
        /// </summary>
        public IoPanelButtonType ButtonType { get; set; } = IoPanelButtonType.Unspecified;    }
}
