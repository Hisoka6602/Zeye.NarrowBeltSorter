namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options {
    /// <summary>
    /// Leadshaine 点位绑定配置集合。
    /// </summary>
    public sealed record LeadshainePointBindingCollectionOptions {
        /// <summary>
        /// 点位绑定集合。
        /// </summary>
        public List<LeadshainePointBindingOptions> Points { get; set; } = new();
    }
}
