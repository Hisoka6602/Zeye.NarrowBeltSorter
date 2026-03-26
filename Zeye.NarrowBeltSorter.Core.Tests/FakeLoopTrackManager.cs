using Zeye.NarrowBeltSorter.Core.Enums.Track;
using Zeye.NarrowBeltSorter.Core.Events.Track;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;

namespace Zeye.NarrowBeltSorter.Core.Tests {

    /// <summary>
    /// LoopTrack 管理器测试桩。
    /// </summary>
    internal sealed class FakeLoopTrackManager : ILoopTrackManager {

        /// <summary>
        /// 启动返回值。
        /// </summary>
        public bool StartResult { get; set; } = true;

        /// <summary>
        /// 设速返回值。
        /// </summary>
        public bool SetTargetSpeedResult { get; set; } = true;

        /// <summary>
        /// 连接调用次数。
        /// </summary>
        public int ConnectCallCount { get; private set; }

        /// <summary>
        /// 启动调用次数。
        /// </summary>
        public int StartCallCount { get; private set; }

        /// <summary>
        /// 设速调用次数。
        /// </summary>
        public int SetTargetSpeedCallCount { get; private set; }

        /// <summary>
        /// 停止调用次数。
        /// </summary>
        public int StopCallCount { get; private set; }

        /// <summary>
        /// 断开调用次数。
        /// </summary>
        public int DisconnectCallCount { get; private set; }

        /// <summary>
        /// 释放调用次数。
        /// </summary>
        public int DisposeCallCount { get; private set; }

        /// <summary>
        /// 自动启动链路方法调用顺序。
        /// </summary>
        public List<string> CallSequence { get; } = [];

        /// <summary>
        /// 连接成功后是否触发连接状态变更事件。
        /// </summary>
        public bool RaiseConnectionStatusChangedOnConnect { get; set; }

        /// <inheritdoc />
        public string TrackName { get; } = "Test-Track";

        /// <inheritdoc />
        public LoopTrackConnectionStatus ConnectionStatus { get; private set; } = LoopTrackConnectionStatus.Disconnected;

        /// <inheritdoc />
        public LoopTrackRunStatus RunStatus { get; private set; } = LoopTrackRunStatus.Stopped;

        /// <inheritdoc />
        public LoopTrackStabilizationStatus StabilizationStatus { get; private set; } = LoopTrackStabilizationStatus.NotStabilized;

        /// <inheritdoc />
        public decimal RealTimeSpeedMmps { get; private set; }

        /// <inheritdoc />
        public decimal TargetSpeedMmps { get; private set; }

        /// <inheritdoc />
        public LoopTrackConnectionOptions ConnectionOptions { get; } = new();

        /// <inheritdoc />
        public LoopTrackPidOptions PidOptions { get; } = new();

        /// <inheritdoc />
        public TimeSpan? StabilizationElapsed { get; private set; }

        /// <inheritdoc />
        public DateTime? PidLastUpdatedAt { get; private set; }

        /// <inheritdoc />
        public decimal PidLastErrorMmps { get; private set; }

        /// <inheritdoc />
        public decimal PidLastProportionalHz { get; private set; }

        /// <inheritdoc />
        public decimal PidLastIntegralHz { get; private set; }

        /// <inheritdoc />
        public decimal PidLastDerivativeHz { get; private set; }

        /// <inheritdoc />
        public decimal PidLastUnclampedHz { get; private set; }

        /// <inheritdoc />
        public decimal PidLastCommandHz { get; private set; }

        /// <inheritdoc />
        public bool PidLastOutputClamped { get; private set; }

        /// <inheritdoc />
        public event EventHandler<LoopTrackConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

        /// <inheritdoc />
        public event EventHandler<LoopTrackRunStatusChangedEventArgs>? RunStatusChanged;

        /// <inheritdoc />
        public event EventHandler<LoopTrackSpeedChangedEventArgs>? SpeedChanged;

        /// <inheritdoc />
        public event EventHandler<LoopTrackStabilizationStatusChangedEventArgs>? StabilizationStatusChanged;

        /// <inheritdoc />
        public event EventHandler<LoopTrackStabilizationResetEventArgs>? StabilizationReset;

        /// <inheritdoc />
        public event EventHandler<LoopTrackTargetSpeedClampedEventArgs>? TargetSpeedClamped;

        /// <inheritdoc />
        public event EventHandler<LoopTrackSpeedNotReachedEventArgs>? SpeedNotReached;

        /// <inheritdoc />
        public event EventHandler<LoopTrackLowFrequencySetpointEventArgs>? LowFrequencySetpointDetected;

        /// <inheritdoc />
        public event EventHandler<LoopTrackSpeedSpreadTooLargeEventArgs>? SpeedSpreadTooLargeDetected;

        /// <inheritdoc />
        public event EventHandler<LoopTrackSpeedSamplingPartiallyFailedEventArgs>? SpeedSamplingPartiallyFailed;

        /// <inheritdoc />
        public event EventHandler<LoopTrackFrequencySetpointHardClampedEventArgs>? FrequencySetpointHardClamped;

        /// <inheritdoc />
        public event EventHandler<LoopTrackManagerFaultedEventArgs>? Faulted;

        /// <inheritdoc />
        public ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default) {
            ConnectCallCount++;
            var previousStatus = ConnectionStatus;
            ConnectionStatus = LoopTrackConnectionStatus.Connected;
            if (RaiseConnectionStatusChangedOnConnect) {
                ConnectionStatusChanged?.Invoke(this, new LoopTrackConnectionStatusChangedEventArgs {
                    OldStatus = previousStatus,
                    NewStatus = ConnectionStatus,
                    // 使用本地时间语义记录事件时间。
                    ChangedAt = DateTime.Now,
                    Message = "Fake manager connect event"
                });
            }

            return ValueTask.FromResult(true);
        }

        /// <inheritdoc />
        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default) {
            DisconnectCallCount++;
            ConnectionStatus = LoopTrackConnectionStatus.Disconnected;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask<bool> SetTargetSpeedAsync(decimal speedMmps, CancellationToken cancellationToken = default) {
            SetTargetSpeedCallCount++;
            CallSequence.Add(nameof(SetTargetSpeedAsync));
            TargetSpeedMmps = speedMmps;
            return ValueTask.FromResult(SetTargetSpeedResult);
        }

        /// <inheritdoc />
        public ValueTask<bool> StartAsync(CancellationToken cancellationToken = default) {
            StartCallCount++;
            CallSequence.Add(nameof(StartAsync));
            RunStatus = StartResult ? LoopTrackRunStatus.Running : LoopTrackRunStatus.Stopped;
            return ValueTask.FromResult(StartResult);
        }

        /// <inheritdoc />
        public ValueTask<bool> StopAsync(CancellationToken cancellationToken = default) {
            StopCallCount++;
            RunStatus = LoopTrackRunStatus.Stopped;
            return ValueTask.FromResult(true);
        }

        /// <inheritdoc />
        public ValueTask<bool> ClearAlarmAsync(CancellationToken cancellationToken = default) {
            return ValueTask.FromResult(true);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() {
            DisposeCallCount++;
            ConnectionStatus = LoopTrackConnectionStatus.Disconnected;
            return ValueTask.CompletedTask;
        }
    }
}
