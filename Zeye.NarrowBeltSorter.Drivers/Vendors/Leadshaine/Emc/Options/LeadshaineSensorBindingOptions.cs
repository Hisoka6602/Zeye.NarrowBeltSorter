using Zeye.NarrowBeltSorter.Core.Enums.Io;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc.Options {
    /// <summary>
    /// Leadshaine 传感器点位绑定配置。
    /// </summary>
    public sealed record LeadshaineSensorBindingOptions : LeadshainePointReferenceOptions {
        /// <summary>
        /// 传感器名称。
        /// </summary>
        public string SensorName { get; set; } = string.Empty;

        /// <summary>
        /// 去抖窗口（毫秒，0 表示不去抖）。
        /// </summary>
        public int DebounceWindowMs { get; set; }

        /// <summary>
        /// 传感器类型（默认：NonFirstCarSensor）。
        /// </summary>
        public IoPointType SensorType { get; set; } = IoPointType.NonFirstCarSensor;
    }
}
