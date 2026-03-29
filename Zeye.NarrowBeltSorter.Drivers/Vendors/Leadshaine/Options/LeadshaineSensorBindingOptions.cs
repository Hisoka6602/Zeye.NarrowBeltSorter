namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options {
    /// <summary>
    /// Leadshaine 传感器点位绑定配置。
    /// </summary>
    public sealed record LeadshaineSensorBindingOptions : LeadshainePointReferenceOptions {
        /// <summary>
        /// 传感器名称。
        /// </summary>
        public string SensorName { get; set; } = string.Empty;
    }
}
