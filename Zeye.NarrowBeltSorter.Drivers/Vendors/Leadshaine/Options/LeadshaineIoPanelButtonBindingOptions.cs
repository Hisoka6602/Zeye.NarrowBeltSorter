namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options {
    /// <summary>
    /// Leadshaine IoPanel 按钮点位绑定配置。
    /// </summary>
    public sealed record LeadshaineIoPanelButtonBindingOptions {
        /// <summary>
        /// 按钮名称。
        /// </summary>
        public string ButtonName { get; set; } = string.Empty;

        /// <summary>
        /// 绑定的 EMC 逻辑点位标识。
        /// </summary>
        public string PointId { get; set; } = string.Empty;
    }
}
