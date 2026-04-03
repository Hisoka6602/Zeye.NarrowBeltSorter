namespace Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine {
    /// <summary>
    /// Leadshaine 传感器点位绑定集合配置。
    /// </summary>
    public sealed record LeadshaineSensorBindingCollectionOptions {
        /// <summary>
        /// 传感器绑定集合。
        /// </summary>
        public List<LeadshaineSensorBindingOptions> Sensors { get; set; } = new();
    }
}
