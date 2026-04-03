using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.Io;
using Zeye.NarrowBeltSorter.Core.Manager.Sensor;
using Zeye.NarrowBeltSorter.Core.Models.Sensor;

namespace Zeye.NarrowBeltSorter.Core.Tests {

    /// <summary>
    /// 传感器管理器测试桩：支持手动触发 SensorStateChanged 事件。
    /// </summary>
    internal sealed class FakeSensorManager : ISensorManager {

        /// <inheritdoc />
        public SensorMonitoringStatus Status { get; } = SensorMonitoringStatus.Stopped;

        /// <inheritdoc />
        public bool IsMonitoring => false;

        /// <inheritdoc />
        public IReadOnlyList<SensorInfo> Sensors => [];

        /// <inheritdoc />
        public event EventHandler<SensorStateChangedEventArgs>? SensorStateChanged;

        /// <inheritdoc />
        public event EventHandler<SensorMonitoringStatusChangedEventArgs>? MonitoringStatusChanged;

        /// <inheritdoc />
        public event EventHandler<SensorFaultedEventArgs>? Faulted;

        /// <inheritdoc />
        public ValueTask StartMonitoringAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        /// <inheritdoc />
        public ValueTask StopMonitoringAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        /// <summary>
        /// 手动触发指定类型传感器的状态变更事件。
        /// </summary>
        /// <param name="sensorType">传感器类型。</param>
        /// <param name="newState">变更后的电平状态。</param>
        public void RaiseSensorStateChanged(IoPointType sensorType, IoState newState) {
            SensorStateChanged?.Invoke(this, new SensorStateChangedEventArgs(
                Point: 0,
                SensorName: sensorType.ToString(),
                SensorType: sensorType,
                OldState: newState == IoState.High ? IoState.Low : IoState.High,
                NewState: newState,
                TriggerState: newState,
                OccurredAtMs: 0));
        }
    }
}
