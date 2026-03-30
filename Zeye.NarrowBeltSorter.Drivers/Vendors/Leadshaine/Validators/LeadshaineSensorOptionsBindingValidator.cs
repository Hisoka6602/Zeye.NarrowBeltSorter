using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc.Options;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Validators {
    /// <summary>
    /// Leadshaine 传感器绑定校验器。
    /// </summary>
    public sealed class LeadshaineSensorOptionsBindingValidator {
        private static readonly string AllowedSensorTypeValues = string.Join('/', Enum.GetNames<IoPointType>());

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
            if (sensorOptions.Sensors is null || sensorOptions.Sensors.Count == 0) {
                return [];
            }

            var errors = PointBindingReferenceValidator.Validate(
                sensorOptions.Sensors,
                validPointIds,
                static x => x.SensorName,
                static x => x.PointId,
                static i => $"Leadshaine.Sensor.Sensors[{i}]",
                "SensorName",
                "Leadshaine.PointBindings.Points").ToList();
            for (var i = 0; i < sensorOptions.Sensors.Count; i++) {
                if (sensorOptions.Sensors[i].DebounceWindowMs < 0) {
                    errors.Add($"Leadshaine.Sensor.Sensors[{i}].DebounceWindowMs 不能为负数。");
                }

                if (sensorOptions.Sensors[i].PollIntervalMs < 0) {
                    errors.Add($"Leadshaine.Sensor.Sensors[{i}].PollIntervalMs 不能为负数。");
                }

                var effectiveSensorType = sensorOptions.Sensors[i].ResolveSensorType();
                if (!Enum.IsDefined(typeof(IoPointType), effectiveSensorType)) {
                    errors.Add($"Leadshaine.Sensor.Sensors[{i}] 的传感器类型配置无效（Type 或 SensorType）：{effectiveSensorType}，允许值：{AllowedSensorTypeValues}。");
                }
            }

            return errors;
        }
    }
}
