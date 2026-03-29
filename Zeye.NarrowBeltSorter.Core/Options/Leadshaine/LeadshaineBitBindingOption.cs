namespace Zeye.NarrowBeltSorter.Core.Options.Leadshaine {
    /// <summary>
    /// Leadshaine 物理位绑定配置。
    /// </summary>
    public sealed record LeadshaineBitBindingOption {
        /// <summary>
        /// 点位区域（Input/Output）。
        /// </summary>
        public string Area { get; set; } = string.Empty;

        /// <summary>
        /// 板卡序号。
        /// </summary>
        public ushort CardNo { get; set; }

        /// <summary>
        /// 端口序号。
        /// </summary>
        public ushort PortNo { get; set; }

        /// <summary>
        /// 位索引（0~31）。
        /// </summary>
        public int BitIndex { get; set; }

        /// <summary>
        /// 触发电平（High/Low）。
        /// </summary>
        public string TriggerState { get; set; } = "High";
    }
}
