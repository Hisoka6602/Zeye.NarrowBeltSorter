using Zeye.NarrowBeltSorter.Core.Enums.Track;
using Zeye.NarrowBeltSorter.Core.Events.Track;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// LoopTrack 管理器测试桩。
    /// </summary>
#pragma warning disable CS0067
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
        /// Connect 调用次数。
        /// </summary>
        public int ConnectCallCount { get; private set; }

        /// <summary>
        /// Stop 调用次数。
        /// </summary>
        public int StopCallCount { get; private set; }

        /// <summary>
        /// Disconnect 调用次数。
        /// </summary>
        public int DisconnectCallCount { get; private set; }

        /// <summary>
        /// Dispose 调用次数。
        /// </summary>
        public int DisposeCallCount { get; private set; }

        /// <summary>继承接口定义。</summary>
        public string TrackName { get; } = "Test-Track";

        /// <summary>继承接口定义。</summary>
        public LoopTrackConnectionStatus ConnectionStatus { get; private set; } = LoopTrackConnectionStatus.Disconnected;

        /// <summary>继承接口定义。</summary>
        public LoopTrackRunStatus RunStatus { get; private set; } = LoopTrackRunStatus.Stopped;

        /// <summary>继承接口定义。</summary>
        public LoopTrackStabilizationStatus StabilizationStatus { get; private set; } = LoopTrackStabilizationStatus.NotStabilized;

        /// <summary>继承接口定义。</summary>
        public decimal RealTimeSpeedMmps { get; private set; }

        /// <summary>继承接口定义。</summary>
        public decimal TargetSpeedMmps { get; private set; }

        /// <summary>继承接口定义。</summary>
        public LoopTrackConnectionOptions ConnectionOptions { get; } = new();

        /// <summary>继承接口定义。</summary>
        public LoopTrackPidOptions PidOptions { get; } = new();

        /// <summary>继承接口定义。</summary>
        public TimeSpan? StabilizationElapsed { get; private set; }

        /// <summary>继承接口定义。</summary>
        public event EventHandler<LoopTrackConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

        /// <summary>继承接口定义。</summary>
        public event EventHandler<LoopTrackRunStatusChangedEventArgs>? RunStatusChanged;

        /// <summary>继承接口定义。</summary>
        public event EventHandler<LoopTrackSpeedChangedEventArgs>? SpeedChanged;

        /// <summary>继承接口定义。</summary>
        public event EventHandler<LoopTrackStabilizationStatusChangedEventArgs>? StabilizationStatusChanged;

        /// <summary>继承接口定义。</summary>
        public event EventHandler<LoopTrackStabilizationResetEventArgs>? StabilizationReset;

        /// <summary>继承接口定义。</summary>
        public event EventHandler<LoopTrackTargetSpeedClampedEventArgs>? TargetSpeedClamped;

        /// <summary>继承接口定义。</summary>
        public event EventHandler<LoopTrackSpeedNotReachedEventArgs>? SpeedNotReached;

        /// <summary>继承接口定义。</summary>
        public event EventHandler<LoopTrackLowFrequencySetpointEventArgs>? LowFrequencySetpointDetected;

        /// <summary>继承接口定义。</summary>
        public event EventHandler<LoopTrackSpeedSpreadTooLargeEventArgs>? SpeedSpreadTooLargeDetected;

        /// <summary>继承接口定义。</summary>
        public event EventHandler<LoopTrackSpeedSamplingPartiallyFailedEventArgs>? SpeedSamplingPartiallyFailed;

        /// <summary>继承接口定义。</summary>
        public event EventHandler<LoopTrackFrequencySetpointHardClampedEventArgs>? FrequencySetpointHardClamped;

        /// <summary>继承接口定义。</summary>
        public event EventHandler<LoopTrackManagerFaultedEventArgs>? Faulted;

        /// <summary>继承接口定义。</summary>
        public ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default) {
            ConnectCallCount++;
            ConnectionStatus = LoopTrackConnectionStatus.Connected;
            return ValueTask.FromResult(true);
        }

        /// <summary>继承接口定义。</summary>
        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default) {
            DisconnectCallCount++;
            ConnectionStatus = LoopTrackConnectionStatus.Disconnected;
            return ValueTask.CompletedTask;
        }

        /// <summary>继承接口定义。</summary>
        public ValueTask<bool> SetTargetSpeedAsync(decimal speedMmps, CancellationToken cancellationToken = default) {
            TargetSpeedMmps = speedMmps;
            return ValueTask.FromResult(SetTargetSpeedResult);
        }

        /// <summary>继承接口定义。</summary>
        public ValueTask<bool> StartAsync(CancellationToken cancellationToken = default) {
            RunStatus = StartResult ? LoopTrackRunStatus.Running : LoopTrackRunStatus.Stopped;
            return ValueTask.FromResult(StartResult);
        }

        /// <summary>继承接口定义。</summary>
        public ValueTask<bool> StopAsync(CancellationToken cancellationToken = default) {
            StopCallCount++;
            RunStatus = LoopTrackRunStatus.Stopped;
            return ValueTask.FromResult(true);
        }

        /// <summary>继承接口定义。</summary>
        public ValueTask<bool> ClearAlarmAsync(CancellationToken cancellationToken = default) {
            return ValueTask.FromResult(true);
        }

        /// <summary>继承接口定义。</summary>
        public ValueTask DisposeAsync() {
            DisposeCallCount++;
            ConnectionStatus = LoopTrackConnectionStatus.Disconnected;
            return ValueTask.CompletedTask;
        }
    }
#pragma warning restore CS0067
}
