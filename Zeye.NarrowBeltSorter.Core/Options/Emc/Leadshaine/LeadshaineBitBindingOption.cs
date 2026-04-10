namespace Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine {
    /// <summary>
    /// Leadshaine 物理位绑定配置。
    /// </summary>
    public sealed record LeadshaineBitBindingOption {
        /// <summary>
        /// 点位区域（可选值：Input/Output）。
        /// </summary>
        public string Area { get; set; } = string.Empty;

        /// <summary>
        /// 板卡序号（范围：0~65535，具体上限由硬件决定）。
        /// </summary>
        public ushort CardNo { get; set; }

        /// <summary>
        /// 端口序号（范围：0~65535，具体上限由硬件决定）。
        /// </summary>
        public ushort PortNo { get; set; }

        /// <summary>
        /// 位索引（范围：0~31）。
        /// </summary>
        public int BitIndex { get; set; }

        /// <summary>
        /// 触发电平（可选值：High/Low）。
        /// </summary>
        public string TriggerState { get; set; } = "High";
    }
}
