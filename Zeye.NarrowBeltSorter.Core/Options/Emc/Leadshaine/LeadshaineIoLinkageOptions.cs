namespace Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine {
    /// <summary>
    /// Leadshaine 联动 IO 配置集合。
    /// </summary>
    public sealed class LeadshaineIoLinkageOptions {
        /// <summary>
        /// 是否启用联动服务。
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 联动点位规则集合。
        /// </summary>
        public List<LeadshaineIoLinkagePointOptions> Points { get; set; } = [];
    }
}
