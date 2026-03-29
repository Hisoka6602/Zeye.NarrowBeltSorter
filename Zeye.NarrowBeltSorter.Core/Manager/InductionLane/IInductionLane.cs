using Zeye.NarrowBeltSorter.Core.Enums.Device;
using Zeye.NarrowBeltSorter.Core.Enums.InductionLane;
using Zeye.NarrowBeltSorter.Core.Events.InductionLane;
using Zeye.NarrowBeltSorter.Core.Models.Sensor;
using Zeye.NarrowBeltSorter.Core.Options.InductionLane;

namespace Zeye.NarrowBeltSorter.Core.Manager.InductionLane {

/// <summary>
/// 供包台接口（描述单路供包台状态与控制能力）
/// </summary>
public interface IInductionLane {
    /// <summary>
    /// 供包台 Id
    /// </summary>
    long Id { get; }

    /// <summary>
    /// 供包台名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 当前连接状态
    /// </summary>
    DeviceConnectionStatus ConnectionStatus { get; }

    /// <summary>
    /// 创建包裹到上车位距离（mm）
    /// </summary>
    decimal ParcelCreatedToLoadingPositionDistanceMm { get; }

    /// <summary>
    /// 当前皮带速度（mm/s）
    /// </summary>
    decimal ConveyorSpeedMmps { get; }

    /// <summary>
    /// 是否首次稳速后再启动
    /// </summary>
    bool StartAfterFirstStableSpeed { get; }

    /// <summary>
    /// 供包台皮带 IO 集合（快照）
    /// </summary>
    IReadOnlyList<SensorInfo> ConveyorIoSensors { get; }

    /// <summary>
    /// 当前供包台状态
    /// </summary>
    InductionLaneStatus Status { get; }

    /// <summary>
    /// 创建包裹 IO
    /// </summary>
    SensorInfo ParcelCreatedIo { get; }

    /// <summary>
    /// 是否监控包裹长度
    /// </summary>
    bool IsMonitoringParcelLength { get; }

    /// <summary>
    /// 包裹创建事件
    /// </summary>
    event EventHandler<InductionLaneParcelCreatedEventArgs>? ParcelCreated;

    /// <summary>
    /// 包裹到达上车位事件
    /// </summary>
    event EventHandler<InductionLaneParcelArrivedAtLoadingPositionEventArgs>? ParcelArrivedAtLoadingPosition;

    /// <summary>
    /// 供包台状态变化事件
    /// </summary>
    event EventHandler<InductionLaneStatusChangedEventArgs>? StatusChanged;

    /// <summary>
    /// 设置供包台配置（设置失败或配置非法时返回 false）
    /// </summary>
    ValueTask<bool> ConfigureAsync(
        InductionLaneOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动供包台（启动失败或状态不允许启动时返回 false）
    /// </summary>
    ValueTask<bool> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止供包台（停止失败或状态不允许停止时返回 false）
    /// </summary>
    ValueTask<bool> StopAsync(
        CancellationToken cancellationToken = default);
}
}
