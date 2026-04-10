namespace Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine {
    /// <summary>
    /// Leadshaine IoPanel 按钮点位绑定集合配置。
    /// </summary>
    public sealed record LeadshaineIoPanelButtonBindingCollectionOptions {
        /// <summary>
        /// 按钮绑定集合，每项对应一个 IoPanel 按钮的点位绑定配置。
        /// </summary>
        public List<LeadshaineIoPanelButtonBindingOptions> Buttons { get; set; } = new();
    }
}
