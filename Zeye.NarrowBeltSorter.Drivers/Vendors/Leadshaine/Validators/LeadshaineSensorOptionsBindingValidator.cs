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
            var validationErrors = new List<string>(4);
            var validPointIds = pointBindingOptions.Points
                .Select(x => x.PointId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < sensorOptions.Sensors.Count; i++) {
                var sensor = sensorOptions.Sensors[i];
                var sensorPath = $"Leadshaine.Sensor.Sensors[{i}]";
                if (string.IsNullOrWhiteSpace(sensor.SensorName)) {
                    validationErrors.Add($"{sensorPath}.SensorName 不能为空。");
                }

                if (string.IsNullOrWhiteSpace(sensor.PointId)) {
                    validationErrors.Add($"{sensorPath}.PointId 不能为空。");
                    continue;
                }

                if (!validPointIds.Contains(sensor.PointId)) {
                    validationErrors.Add($"{sensorPath}.PointId={sensor.PointId} 未在 Leadshaine.PointBindings.Points 中定义。");
                }
            }

            return validationErrors;
        }
    }
}
