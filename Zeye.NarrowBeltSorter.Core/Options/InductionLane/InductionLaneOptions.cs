using Zeye.NarrowBeltSorter.Core.Models.Sensor;

namespace Zeye.NarrowBeltSorter.Core.Options.InductionLane {

    /// <summary>
    /// 供包台配置。
    /// </summary>
    public sealed record InductionLaneOptions {
        /// <summary>
        /// 创建包裹到上车位距离最小值（mm）。
        /// </summary>
        public const decimal MinParcelCreatedToLoadingPositionDistanceMm = 1;

        /// <summary>
        /// 皮带速度最小值（mm/s）。
        /// </summary>
        public const decimal MinConveyorSpeedMmps = 1;

        /// <summary>
        /// 创建包裹到上车位距离（单位：mm，最小值：1，建议根据现场实测配置）。
        /// </summary>
        public decimal ParcelCreatedToLoadingPositionDistanceMm { get; set; } = 500;

        /// <summary>
        /// 皮带速度（单位：mm/s，最小值：1，建议范围：100~3000）。
        /// </summary>
        public decimal ConveyorSpeedMmps { get; set; } = 1000;

        /// <summary>
        /// 是否首次稳速后再启动（取值：true/false）。
        /// </summary>
        public bool StartAfterFirstStableSpeed { get; set; }

        /// <summary>
        /// 供包台皮带 IO 集合，每项对应一个皮带 IO 传感器配置（至少需要配置一个）。
        /// </summary>
        public IReadOnlyList<SensorInfo> ConveyorIoSensors { get; set; } = Array.Empty<SensorInfo>();

        /// <summary>
        /// 创建包裹 IO。
        /// </summary>
        public SensorInfo? ParcelCreatedIo { get; set; }

        /// <summary>
        /// 是否监控包裹长度（取值：true/false；true 时启用包裹长度采样与记录功能）。
        /// </summary>
        public bool IsMonitoringParcelLength { get; set; }

        /// <summary>
        /// 校验配置边界与必填项。
        /// </summary>
        /// <returns>当配置有效时返回空集合；当配置无效时返回包含错误描述的只读集合。</returns>
        public IReadOnlyList<string> Validate() {
            var errors = new List<string>();
            if (ParcelCreatedToLoadingPositionDistanceMm < MinParcelCreatedToLoadingPositionDistanceMm) {
                errors.Add($"创建包裹到上车位距离最小值为 {MinParcelCreatedToLoadingPositionDistanceMm}mm，当前值：{ParcelCreatedToLoadingPositionDistanceMm}mm。");
            }

            if (ConveyorSpeedMmps < MinConveyorSpeedMmps) {
                errors.Add($"皮带速度最小值为 {MinConveyorSpeedMmps}mm/s，当前值：{ConveyorSpeedMmps}mm/s。");
            }

            if (ConveyorIoSensors.Count == 0) {
                errors.Add("供包台皮带 IO 集合不能为空，至少需要配置一个 IO 点。");
            }

            if (ParcelCreatedIo is null) {
                errors.Add("创建包裹 IO 不能为空。");
            }

            return errors;
        }
    }
}
