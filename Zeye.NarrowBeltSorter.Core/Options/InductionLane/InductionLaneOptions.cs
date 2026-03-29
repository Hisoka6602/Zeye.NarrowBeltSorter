using Zeye.NarrowBeltSorter.Core.Models.Sensor;

namespace Zeye.NarrowBeltSorter.Core.Options.InductionLane {

    /// <summary>
    /// 供包台配置。
    /// </summary>
    public sealed class InductionLaneOptions {
        /// <summary>
        /// 创建包裹到上车位距离（mm）。
        /// </summary>
        public required decimal ParcelCreatedToLoadingPositionDistanceMm { get; init; }

        /// <summary>
        /// 皮带速度（mm/s）。
        /// </summary>
        public required decimal ConveyorSpeedMmps { get; init; }

        /// <summary>
        /// 是否首次稳速后再启动。
        /// </summary>
        public bool StartAfterFirstStableSpeed { get; init; }

        /// <summary>
        /// 供包台皮带 IO 集合。
        /// </summary>
        public required IReadOnlyList<SensorInfo> ConveyorIoSensors { get; init; }

        /// <summary>
        /// 创建包裹 IO。
        /// </summary>
        public required SensorInfo ParcelCreatedIo { get; init; }

        /// <summary>
        /// 是否监控包裹长度。
        /// </summary>
        public bool IsMonitoringParcelLength { get; init; }
    }
}
