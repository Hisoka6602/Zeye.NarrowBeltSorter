namespace Zeye.NarrowBeltSorter.Core.Options.Leadshaine {
    /// <summary>
    /// 单个 Leadshaine 逻辑点位绑定配置。
    /// </summary>
    public sealed record LeadshaineIoPointBindingOption {
        /// <summary>
        /// 逻辑点位标识。
        /// </summary>
        public string PointId { get; set; } = string.Empty;

        /// <summary>
        /// 物理位绑定配置。
        /// </summary>
        public LeadshaineBitBindingOption Binding { get; set; } = new();
    }
}
