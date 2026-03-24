using Zeye.NarrowBeltSorter.Core.Enums.Track;
using Zeye.NarrowBeltSorter.Core.Events.Track;
using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;

namespace Zeye.NarrowBeltSorter.Core.Manager.TrackSegment {

    /// <summary>
    /// 环形轨道管理器（单环单动力线/滑触线场景）
    /// </summary>
    public interface ILoopTrackManager : IAsyncDisposable {

        /// <summary>
        /// 轨道名称
        /// </summary>
        string TrackName { get; }

        /// <summary>
        /// 当前连接状态
        /// </summary>
        LoopTrackConnectionStatus ConnectionStatus { get; }

        /// <summary>
        /// 当前运行状态
        /// </summary>
        LoopTrackRunStatus RunStatus { get; }

        /// <summary>
        /// 当前稳速状态
        /// </summary>
        LoopTrackStabilizationStatus StabilizationStatus { get; }

        /// <summary>
        /// 实时速度（单位：mm/s）
        /// </summary>
        decimal RealTimeSpeedMmps { get; }

        /// <summary>
        /// 目标速度（单位：mm/s）
        /// </summary>
        decimal TargetSpeedMmps { get; }

        /// <summary>
        /// 连接参数快照
        /// </summary>
        LoopTrackConnectionOptions ConnectionOptions { get; }

        /// <summary>
        /// PID 参数快照
        /// </summary>
        LoopTrackPidOptions PidOptions { get; }

        /// <summary>
        /// 稳速耗时（未进入稳速流程时为 null）
        /// </summary>
        TimeSpan? StabilizationElapsed { get; }

        /// <summary>
        /// 最近一次 PID 计算时间（本地时间，未计算时为 null）。
        /// </summary>
        DateTime? PidLastUpdatedAt { get; }

        /// <summary>
        /// 最近一次 PID 速度误差（mm/s）。
        /// </summary>
        decimal PidLastErrorMmps { get; }

        /// <summary>
        /// 最近一次 PID 比例项贡献（Hz）。
        /// </summary>
        decimal PidLastProportionalHz { get; }

        /// <summary>
        /// 最近一次 PID 积分项贡献（Hz）。
        /// </summary>
        decimal PidLastIntegralHz { get; }

        /// <summary>
        /// 最近一次 PID 微分项贡献（Hz）。
        /// </summary>
        decimal PidLastDerivativeHz { get; }

        /// <summary>
        /// 最近一次 PID 限幅前输出（Hz）。
        /// </summary>
        decimal PidLastUnclampedHz { get; }

        /// <summary>
        /// 最近一次 PID 限幅后命令输出（Hz）。
        /// </summary>
        decimal PidLastCommandHz { get; }

        /// <summary>
        /// 最近一次 PID 输出是否触发限幅。
        /// </summary>
        bool PidLastOutputClamped { get; }

        /// <summary>
        /// 连接状态变更事件
        /// </summary>
        event EventHandler<LoopTrackConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

        /// <summary>
        /// 运行状态变更事件
        /// </summary>
        event EventHandler<LoopTrackRunStatusChangedEventArgs>? RunStatusChanged;

        /// <summary>
        /// 速度变更事件
        /// </summary>
        event EventHandler<LoopTrackSpeedChangedEventArgs>? SpeedChanged;

        /// <summary>
        /// 稳速状态变更事件
        /// </summary>
        event EventHandler<LoopTrackStabilizationStatusChangedEventArgs>? StabilizationStatusChanged;

        /// <summary>
        /// 稳速状态重置事件
        /// </summary>
        event EventHandler<LoopTrackStabilizationResetEventArgs>? StabilizationReset;

        /// <summary>
        /// 目标速度超出可达上限并被限幅事件
        /// </summary>
        event EventHandler<LoopTrackTargetSpeedClampedEventArgs>? TargetSpeedClamped;

        /// <summary>
        /// 速度长时间未达到目标事件
        /// </summary>
        event EventHandler<LoopTrackSpeedNotReachedEventArgs>? SpeedNotReached;

        /// <summary>
        /// 频率给定偏低事件
        /// </summary>
        event EventHandler<LoopTrackLowFrequencySetpointEventArgs>? LowFrequencySetpointDetected;

        /// <summary>
        /// 多从站速度差异过大事件
        /// </summary>
        event EventHandler<LoopTrackSpeedSpreadTooLargeEventArgs>? SpeedSpreadTooLargeDetected;

        /// <summary>
        /// 速度采样部分失败事件
        /// </summary>
        event EventHandler<LoopTrackSpeedSamplingPartiallyFailedEventArgs>? SpeedSamplingPartiallyFailed;

        /// <summary>
        /// 频率给定被保护上限限幅事件
        /// </summary>
        event EventHandler<LoopTrackFrequencySetpointHardClampedEventArgs>? FrequencySetpointHardClamped;

        /// <summary>
        /// 管理器异常事件（用于隔离异常，不影响上层调用链）
        /// </summary>
        event EventHandler<LoopTrackManagerFaultedEventArgs>? Faulted;

        /// <summary>
        /// 连接
        /// </summary>
        ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 断开连接
        /// </summary>
        ValueTask DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置目标速度（单位：mm/s）
        /// </summary>
        ValueTask<bool> SetTargetSpeedAsync(decimal speedMmps, CancellationToken cancellationToken = default);

        /// <summary>
        /// 启动轨道
        /// </summary>
        ValueTask<bool> StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止轨道
        /// </summary>
        ValueTask<bool> StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 清除变频器报警/故障
        /// </summary>
        ValueTask<bool> ClearAlarmAsync(CancellationToken cancellationToken = default);
    }
}
