namespace Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine {
    /// <summary>
    /// Leadshaine 逻辑点位绑定配置集合。
    /// </summary>
    public sealed record LeadshaineIoPointBindingCollectionOptions {
        /// <summary>
        /// EMC 监控与控制点位绑定集合。
        /// </summary>
        public List<LeadshaineIoPointBindingOption> Points { get; set; } = new();
    }
}
