namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options {
    /// <summary>
    /// Leadshaine 逻辑点位绑定配置。
    /// </summary>
    public sealed record LeadshainePointBindingOptions {
        /// <summary>
        /// 逻辑点位标识。
        /// </summary>
        public string PointId { get; set; } = string.Empty;

        /// <summary>
        /// 物理位绑定配置。
        /// </summary>
        public LeadshaineBitBindingOptions Binding { get; set; } = new();
    }
}
