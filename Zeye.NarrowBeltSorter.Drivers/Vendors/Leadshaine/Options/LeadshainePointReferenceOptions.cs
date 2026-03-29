namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options {
    /// <summary>
    /// Leadshaine 点位引用基础配置。
    /// </summary>
    public abstract record LeadshainePointReferenceOptions {
        /// <summary>
        /// 绑定的 EMC 逻辑点位标识。
        /// </summary>
        public string PointId { get; set; } = string.Empty;
    }
}
