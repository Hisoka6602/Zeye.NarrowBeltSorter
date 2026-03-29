using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Validators {
    /// <summary>
    /// Leadshaine 传感器绑定校验器。
    /// </summary>
    public sealed class LeadshaineSensorOptionsBindingValidator {
        /// <summary>
        /// 校验传感器绑定是否引用有效点位。
        /// </summary>
        /// <param name="sensorOptions">传感器绑定集合。</param>
        /// <param name="pointBindingOptions">点位绑定集合。</param>
        /// <returns>配置错误集合。</returns>
        public IReadOnlyList<string> Validate(
            LeadshaineSensorBindingCollectionOptions sensorOptions,
            LeadshainePointBindingCollectionOptions pointBindingOptions) {
            var validPointIds = pointBindingOptions.Points
                .Select(x => x.PointId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return LeadshainePointReferenceBindingValidator.Validate(
                sensorOptions.Sensors,
                validPointIds,
                static x => x.SensorName,
                static x => x.PointId,
                static i => $"Leadshaine.Sensor.Sensors[{i}]",
                "SensorName");
        }
    }
}
