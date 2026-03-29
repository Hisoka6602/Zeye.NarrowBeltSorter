namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options {
    /// <summary>
    /// Leadshaine IoPanel 按钮点位绑定集合配置。
    /// </summary>
    public sealed record LeadshaineIoPanelButtonBindingCollectionOptions {
        /// <summary>
        /// 按钮绑定集合。
        /// </summary>
        public List<LeadshaineIoPanelButtonBindingOptions> Buttons { get; set; } = new();
    }
}
