namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc.Options {
    /// <summary>
    /// Leadshaine IoPanel 按钮点位绑定配置。
    /// </summary>
    public sealed record LeadshaineIoPanelButtonBindingOptions : LeadshainePointReferenceOptions {
        /// <summary>
        /// 按钮名称。
        /// </summary>
        public string ButtonName { get; set; } = string.Empty;
    }
}
