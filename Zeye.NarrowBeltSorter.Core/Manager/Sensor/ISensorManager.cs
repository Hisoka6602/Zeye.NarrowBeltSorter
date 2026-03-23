using Zeye.NarrowBeltSorter.Core.Events.Io;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Models.Sensor;

namespace Zeye.NarrowBeltSorter.Core.Manager.Sensor {

    public interface ISensorManager {

        /// <summary>
        /// 当前监控状态
        /// </summary>
        SensorMonitoringStatus Status { get; }

        /// <summary>
        /// 是否正在监控
        /// </summary>
        bool IsMonitoring { get; }

        /// <summary>
        /// 当前监控的传感器配置集合（未启动监控时为空）
        /// </summary>
        IReadOnlyList<SensorInfo> Sensors { get; }

        /// <summary>
        /// 传感器电平改变事件（包含点位、电平、时间戳等）
        /// </summary>
        event EventHandler<SensorStateChangedEventArgs>? SensorStateChanged;

        /// <summary>
        /// 监控状态变更事件
        /// </summary>
        event EventHandler<SensorMonitoringStatusChangedEventArgs>? MonitoringStatusChanged;

        /// <summary>
        /// 异常事件（用于隔离异常，不影响上层调用链）
        /// </summary>
        event EventHandler<SensorFaultedEventArgs>? Faulted;

        /// <summary>
        /// 启动监控（批量传感器配置）
        /// </summary>
        ValueTask StartMonitoringAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止监控
        /// </summary>
        ValueTask StopMonitoringAsync(CancellationToken cancellationToken = default);
    }
}
