using Zeye.NarrowBeltSorter.Core.Models.Sensor;

namespace Zeye.NarrowBeltSorter.Core.Options.InductionLane {

    /// <summary>
    /// 供包台配置。
    /// </summary>
    public sealed record InductionLaneOptions {
        /// <summary>
        /// 创建包裹到上车位距离（mm）。
        /// </summary>
        public decimal ParcelCreatedToLoadingPositionDistanceMm { get; set; } = 1;

        /// <summary>
        /// 皮带速度（mm/s）。
        /// </summary>
        public decimal ConveyorSpeedMmps { get; set; } = 1;

        /// <summary>
        /// 是否首次稳速后再启动。
        /// </summary>
        public bool StartAfterFirstStableSpeed { get; set; } = false;

        /// <summary>
        /// 供包台皮带 IO 集合。
        /// </summary>
        public IReadOnlyList<SensorInfo> ConveyorIoSensors { get; set; } = new List<SensorInfo>();

        /// <summary>
        /// 创建包裹 IO。
        /// </summary>
        public SensorInfo? ParcelCreatedIo { get; set; }

        /// <summary>
        /// 是否监控包裹长度。
        /// </summary>
        public bool IsMonitoringParcelLength { get; set; } = false;

        /// <summary>
        /// 校验配置边界与必填项。
        /// </summary>
        /// <returns>当配置有效时返回空集合；当配置无效时返回包含错误描述的只读集合。</returns>
        public IReadOnlyList<string> Validate() {
            var errors = new List<string>();
            if (ParcelCreatedToLoadingPositionDistanceMm <= 0) {
                errors.Add($"ParcelCreatedToLoadingPositionDistanceMm 最小值为 1，当前值：{ParcelCreatedToLoadingPositionDistanceMm}。");
            }

            if (ConveyorSpeedMmps <= 0) {
                errors.Add($"ConveyorSpeedMmps 最小值为 1，当前值：{ConveyorSpeedMmps}。");
            }

            if (ConveyorIoSensors.Count == 0) {
                errors.Add("ConveyorIoSensors 不能为空，至少需要配置一个 IO 点。");
            }

            if (ParcelCreatedIo is null) {
                errors.Add("ParcelCreatedIo 不能为空。");
            }

            return errors;
        }
    }
}
