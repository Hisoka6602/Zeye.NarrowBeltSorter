using Zeye.NarrowBeltSorter.Core.Enums.Track;
using Zeye.NarrowBeltSorter.Core.Events.Track;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Utilities;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa {
    /// <summary>
    /// 雷玛 LM1000H 环形轨道管理器实现。
    /// </summary>
    public sealed class LeiMaLoopTrackManager : ILoopTrackManager {
        private readonly object _stateLock = new();
        private readonly ILeiMaModbusClientAdapter _modbusClient;
        private readonly LeiMaExecutionGuard _executionGuard;
        private readonly decimal _maxOutputHz;
        private readonly ushort _maxTorqueRawUnit;
        private readonly TimeSpan _pollingInterval;
        private readonly decimal _stabilizedToleranceMmps;
        private readonly decimal _runningFrequencyLowThresholdHz;

        private CancellationTokenSource? _pollingCts;
        private Task? _pollingTask;
        private bool _disposed;
        private DateTime? _stabilizationStartedAt;

        /// <summary>
        /// 初始化雷玛环形轨道管理器。
        /// </summary>
        /// <param name="trackName">轨道名称。</param>
        /// <param name="modbusClient">Modbus 客户端。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <param name="connectionOptions">连接配置。</param>
        /// <param name="pidOptions">PID 配置。</param>
        /// <param name="maxOutputHz">最大输出频率（Hz）。</param>
        /// <param name="maxTorqueRawUnit">P3.10 转矩给定最大原始值。</param>
        /// <param name="pollingInterval">轮询周期。</param>
        /// <param name="stabilizedToleranceMmps">稳速判定容差（mm/s）。</param>
        /// <param name="runningFrequencyLowThresholdHz">低频告警阈值（Hz）。</param>
        public LeiMaLoopTrackManager(
            string trackName,
            ILeiMaModbusClientAdapter modbusClient,
            SafeExecutor safeExecutor,
            LoopTrackConnectionOptions? connectionOptions = null,
            LoopTrackPidOptions? pidOptions = null,
            decimal maxOutputHz = 50m,
            ushort maxTorqueRawUnit = 1000,
            TimeSpan? pollingInterval = null,
            decimal stabilizedToleranceMmps = 50m,
            decimal runningFrequencyLowThresholdHz = 0.5m) {
            if (string.IsNullOrWhiteSpace(trackName)) {
                throw new ArgumentException("轨道名称不能为空。", nameof(trackName));
            }

            if (maxOutputHz <= 0m) {
                throw new ArgumentOutOfRangeException(nameof(maxOutputHz), "最大输出频率必须大于 0。");
            }

            TrackName = trackName;
            _modbusClient = modbusClient ?? throw new ArgumentNullException(nameof(modbusClient));
            _maxOutputHz = maxOutputHz;
            _maxTorqueRawUnit = maxTorqueRawUnit;
            _pollingInterval = pollingInterval ?? TimeSpan.FromMilliseconds(300);
            _stabilizedToleranceMmps = Math.Max(0m, stabilizedToleranceMmps);
            _runningFrequencyLowThresholdHz = Math.Max(0m, runningFrequencyLowThresholdHz);

            ConnectionOptions = connectionOptions ?? new LoopTrackConnectionOptions();
            PidOptions = pidOptions ?? new LoopTrackPidOptions();
            ConnectionStatus = LoopTrackConnectionStatus.Disconnected;
            RunStatus = LoopTrackRunStatus.Stopped;
            StabilizationStatus = LoopTrackStabilizationStatus.NotStabilized;

            _executionGuard = new LeiMaExecutionGuard(
                safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor)),
                PublishFaultEvent);
        }

        /// <inheritdoc />
        public string TrackName { get; }

        /// <inheritdoc />
        public LoopTrackConnectionStatus ConnectionStatus { get; private set; }

        /// <inheritdoc />
        public LoopTrackRunStatus RunStatus { get; private set; }

        /// <inheritdoc />
        public LoopTrackStabilizationStatus StabilizationStatus { get; private set; }

        /// <inheritdoc />
        public decimal RealTimeSpeedMmps { get; private set; }

        /// <inheritdoc />
        public decimal TargetSpeedMmps { get; private set; }

        /// <inheritdoc />
        public LoopTrackConnectionOptions ConnectionOptions { get; }

        /// <inheritdoc />
        public LoopTrackPidOptions PidOptions { get; }

        /// <inheritdoc />
        public TimeSpan? StabilizationElapsed { get; private set; }

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
        public async ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default) {
            ThrowIfDisposed();

            if (ConnectionStatus == LoopTrackConnectionStatus.Connected && _modbusClient.IsConnected) {
                return true;
            }

            // 步骤1：先切换到连接中状态，确保外部可观测到链路建立过程。
            SetConnectionStatus(LoopTrackConnectionStatus.Connecting, "正在连接雷玛变频器。");

            // 步骤2：建立 Modbus 链路并至少读取一次运行状态/故障码验证链路有效性。
            var connected = await _executionGuard.ExecuteAsync(
                "LeiMa.ConnectAsync",
                async token => {
                    await _modbusClient.ConnectAsync(token).ConfigureAwait(false);
                    var runStatus = await _modbusClient.ReadHoldingRegisterAsync(LeiMaRegisters.RunStatus, token).ConfigureAwait(false);
                    var alarm = await _modbusClient.ReadHoldingRegisterAsync(LeiMaRegisters.AlarmCode, token).ConfigureAwait(false);
                    UpdateRunStatusFromRaw(alarm, runStatus, "连接完成状态同步");
                },
                cancellationToken).ConfigureAwait(false);

            if (!connected) {
                SetConnectionStatus(LoopTrackConnectionStatus.Faulted, "连接失败。");
                return false;
            }

            // 步骤3：连接成功后切换状态并启动后台轮询。
            SetConnectionStatus(LoopTrackConnectionStatus.Connected, "连接成功。");
            StartPollingLoop();
            return true;
        }

        /// <inheritdoc />
        public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default) {
            await StopPollingLoopAsync().ConfigureAwait(false);

            await _executionGuard.ExecuteAsync(
                "LeiMa.DisconnectAsync",
                async token => {
                    if (_modbusClient.IsConnected) {
                        await _modbusClient.DisconnectAsync(token).ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);

            SetConnectionStatus(LoopTrackConnectionStatus.Disconnected, "连接已断开。");
            SetRunStatus(LoopTrackRunStatus.Stopped, "断链后状态置停止。");
            ResetStabilization("断开连接重置稳速状态。");
        }

        /// <inheritdoc />
        public async ValueTask<bool> SetTargetSpeedAsync(decimal speedMmps, CancellationToken cancellationToken = default) {
            ThrowIfDisposed();

            if (ConnectionStatus != LoopTrackConnectionStatus.Connected || !_modbusClient.IsConnected) {
                return false;
            }

            var maxSpeedMmps = LeiMaSpeedConverter.HzToMmps(_maxOutputHz);
            var normalized = Math.Clamp(speedMmps, 0m, maxSpeedMmps);
            if (normalized != speedMmps) {
                RaiseEventSafely(
                    TargetSpeedClamped,
                    nameof(TargetSpeedClamped),
                    new LoopTrackTargetSpeedClampedEventArgs {
                        Operation = nameof(SetTargetSpeedAsync),
                        RequestedMmps = speedMmps,
                        LimitedMmps = normalized,
                        ClampMaxHz = _maxOutputHz,
                        MmpsPerHz = LeiMaSpeedConverter.MmpsPerHz,
                        OccurredAt = DateTime.Now
                    });
            }

            // 步骤1：外部输入单位是 mm/s，先严格转换为 Hz 口径。
            var targetHz = LeiMaSpeedConverter.MmpsToHz(normalized);
            var requestedHz = LeiMaSpeedConverter.MmpsToHz(speedMmps);

            // 步骤2：按 ZakYip 已验证路径写 P3.10（转矩给定值）。
            var torqueRaw = LeiMaSpeedConverter.MmpsToTorqueRawUnit(normalized, _maxOutputHz, _maxTorqueRawUnit);
            if (requestedHz > _maxOutputHz) {
                RaiseEventSafely(
                    FrequencySetpointHardClamped,
                    nameof(FrequencySetpointHardClamped),
                    new LoopTrackFrequencySetpointHardClampedEventArgs {
                        RequestedRawUnit = LeiMaSpeedConverter.HzToRawUnit(requestedHz),
                        RequestedHz = requestedHz,
                        ClampMaxHz = _maxOutputHz,
                        ClampedRawUnit = LeiMaSpeedConverter.HzToRawUnit(targetHz),
                        OccurredAt = DateTime.Now
                    });
            }

            if (targetHz > 0m && targetHz < _runningFrequencyLowThresholdHz) {
                RaiseEventSafely(
                    LowFrequencySetpointDetected,
                    nameof(LowFrequencySetpointDetected),
                    new LoopTrackLowFrequencySetpointEventArgs {
                        EstimatedMmps = normalized,
                        RawUnit = torqueRaw,
                        TargetHz = targetHz,
                        ThresholdHz = _runningFrequencyLowThresholdHz,
                        OccurredAt = DateTime.Now
                    });
            }

            var written = await _executionGuard.ExecuteAsync(
                "LeiMa.SetTargetSpeedAsync",
                token => _modbusClient.WriteSingleRegisterAsync(LeiMaRegisters.TorqueSetpoint, torqueRaw, token),
                cancellationToken).ConfigureAwait(false);

            if (!written) {
                return false;
            }

            TargetSpeedMmps = normalized;
            UpdateStabilizationState("目标速度变更");
            return true;
        }

        /// <inheritdoc />
        public async ValueTask<bool> StartAsync(CancellationToken cancellationToken = default) {
            ThrowIfDisposed();
            if (ConnectionStatus != LoopTrackConnectionStatus.Connected || !_modbusClient.IsConnected) {
                return false;
            }

            var success = await _executionGuard.ExecuteAsync(
                "LeiMa.StartAsync",
                token => _modbusClient.WriteSingleRegisterAsync(
                    LeiMaRegisters.Command,
                    LeiMaRegisters.CommandForwardRun,
                    token),
                cancellationToken).ConfigureAwait(false);

            if (success) {
                SetRunStatus(LoopTrackRunStatus.Running, "已下发正转运行命令。");
            }

            return success;
        }

        /// <inheritdoc />
        public async ValueTask<bool> StopAsync(CancellationToken cancellationToken = default) {
            ThrowIfDisposed();
            if (ConnectionStatus != LoopTrackConnectionStatus.Connected || !_modbusClient.IsConnected) {
                return false;
            }

            var success = await _executionGuard.ExecuteAsync(
                "LeiMa.StopAsync",
                token => _modbusClient.WriteSingleRegisterAsync(
                    LeiMaRegisters.Command,
                    LeiMaRegisters.CommandDecelerateStop,
                    token),
                cancellationToken).ConfigureAwait(false);

            if (success) {
                SetRunStatus(LoopTrackRunStatus.Stopped, "已下发减速停机命令。");
                ResetStabilization("停止命令触发稳速重置。");
            }

            return success;
        }

        /// <inheritdoc />
        public async ValueTask<bool> ClearAlarmAsync(CancellationToken cancellationToken = default) {
            ThrowIfDisposed();
            if (ConnectionStatus != LoopTrackConnectionStatus.Connected || !_modbusClient.IsConnected) {
                return false;
            }

            var resetSuccess = await _executionGuard.ExecuteAsync(
                "LeiMa.ClearAlarmAsync.WriteResetCommand",
                token => _modbusClient.WriteSingleRegisterAsync(
                    LeiMaRegisters.Command,
                    LeiMaRegisters.CommandAlarmReset,
                    token),
                cancellationToken).ConfigureAwait(false);

            if (!resetSuccess) {
                return false;
            }

            var (readSuccess, alarmCode) = await _executionGuard.ExecuteAsync(
                "LeiMa.ClearAlarmAsync.ReadBackAlarm",
                token => _modbusClient.ReadHoldingRegisterAsync(LeiMaRegisters.AlarmCode, token),
                (ushort)ushort.MaxValue,
                cancellationToken).ConfigureAwait(false);

            if (!readSuccess) {
                return false;
            }

            if (alarmCode == 0) {
                SetRunStatus(LoopTrackRunStatus.Stopped, "故障复位完成。");
                return true;
            }

            SetRunStatus(LoopTrackRunStatus.Faulted, $"故障未清除，故障码={alarmCode}。");
            return false;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            await StopPollingLoopAsync().ConfigureAwait(false);
            await DisconnectAsync().ConfigureAwait(false);
            await _modbusClient.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 启动后台轮询任务。
        /// </summary>
        private void StartPollingLoop() {
            if (_pollingTask is not null) {
                return;
            }

            var pollingCts = new CancellationTokenSource();
            _pollingCts = pollingCts;
            _pollingTask = Task.Run(() => PollingLoopAsync(pollingCts.Token));
        }

        /// <summary>
        /// 停止后台轮询任务。
        /// </summary>
        private async Task StopPollingLoopAsync() {
            var cts = _pollingCts;
            var task = _pollingTask;

            _pollingCts = null;
            _pollingTask = null;

            if (cts is null || task is null) {
                return;
            }

            try {
                cts.Cancel();
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
            }
            finally {
                cts.Dispose();
            }
        }

        /// <summary>
        /// 执行轮询循环。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task PollingLoopAsync(CancellationToken cancellationToken) {
            using var timer = new PeriodicTimer(_pollingInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) {
                await PollOnceAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 执行一次状态采样轮询。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task PollOnceAsync(CancellationToken cancellationToken) {
            // 步骤1：采集运行状态、故障码与速度来源寄存器。
            var (runOk, runRaw) = await _executionGuard.ExecuteAsync(
                "LeiMa.Poll.RunStatus",
                token => _modbusClient.ReadHoldingRegisterAsync(LeiMaRegisters.RunStatus, token),
                (ushort)3,
                cancellationToken).ConfigureAwait(false);

            var (alarmOk, alarmRaw) = await _executionGuard.ExecuteAsync(
                "LeiMa.Poll.AlarmCode",
                token => _modbusClient.ReadHoldingRegisterAsync(LeiMaRegisters.AlarmCode, token),
                (ushort)0,
                cancellationToken).ConfigureAwait(false);

            var (encoderOk, encoderRaw) = await _executionGuard.ExecuteAsync(
                "LeiMa.Poll.EncoderSpeed",
                token => _modbusClient.ReadHoldingRegisterAsync(LeiMaRegisters.EncoderFeedbackSpeed, token),
                (ushort)0,
                cancellationToken).ConfigureAwait(false);

            var (runningFreqOk, runningFreqRaw) = await _executionGuard.ExecuteAsync(
                "LeiMa.Poll.RunningFrequency",
                token => _modbusClient.ReadHoldingRegisterAsync(LeiMaRegisters.RunningFrequency, token),
                (ushort)0,
                cancellationToken).ConfigureAwait(false);

            var speedSampleSuccessCount = (encoderOk ? 1 : 0) + (runningFreqOk ? 1 : 0);
            if (speedSampleSuccessCount is > 0 and < 2) {
                RaiseEventSafely(
                    SpeedSamplingPartiallyFailed,
                    nameof(SpeedSamplingPartiallyFailed),
                    new LoopTrackSpeedSamplingPartiallyFailedEventArgs {
                        SuccessCount = speedSampleSuccessCount,
                        FailCount = 2 - speedSampleSuccessCount,
                        OccurredAt = DateTime.Now
                    });
            }

            if (runOk || alarmOk) {
                UpdateRunStatusFromRaw(alarmRaw, runRaw, "轮询状态同步");
            }

            if (speedSampleSuccessCount <= 0) {
                return;
            }

            // 步骤2：按优先级换算有效速度并同步实时速度状态。
            var speedHz = encoderOk
                ? LeiMaSpeedConverter.RawUnitToHz(encoderRaw)
                : LeiMaSpeedConverter.RawUnitToHz(runningFreqRaw);
            var speedMmps = LeiMaSpeedConverter.HzToMmps(speedHz);

            if (encoderOk && runningFreqOk) {
                var encoderMmps = LeiMaSpeedConverter.HzToMmps(LeiMaSpeedConverter.RawUnitToHz(encoderRaw));
                var runningMmps = LeiMaSpeedConverter.HzToMmps(LeiMaSpeedConverter.RawUnitToHz(runningFreqRaw));
                var spreadMmps = Math.Abs(encoderMmps - runningMmps);
                if (spreadMmps > _stabilizedToleranceMmps) {
                    RaiseEventSafely(
                        SpeedSpreadTooLargeDetected,
                        nameof(SpeedSpreadTooLargeDetected),
                        new LoopTrackSpeedSpreadTooLargeEventArgs {
                            Strategy = SpeedAggregateStrategy.Min,
                            SpreadMmps = spreadMmps,
                            Samples = $"Encoder={encoderMmps:F2};Running={runningMmps:F2}",
                            OccurredAt = DateTime.Now
                        });
                }
            }

            UpdateRealTimeSpeed(speedMmps);
        }

        /// <summary>
        /// 根据原始状态更新运行状态。
        /// </summary>
        /// <param name="alarmRaw">故障码。</param>
        /// <param name="runRaw">运行状态原始值。</param>
        /// <param name="message">状态说明。</param>
        private void UpdateRunStatusFromRaw(ushort alarmRaw, ushort runRaw, string message) {
            if (alarmRaw != 0) {
                SetRunStatus(LoopTrackRunStatus.Faulted, $"{message}：故障码={alarmRaw}。");
                return;
            }

            var status = runRaw switch {
                1 or 2 => LoopTrackRunStatus.Running,
                3 => LoopTrackRunStatus.Stopped,
                _ => LoopTrackRunStatus.Stopped
            };
            SetRunStatus(status, message);
        }

        /// <summary>
        /// 更新实时速度并触发相关事件。
        /// </summary>
        /// <param name="newSpeedMmps">新速度值。</param>
        private void UpdateRealTimeSpeed(decimal newSpeedMmps) {
            var oldSpeed = RealTimeSpeedMmps;
            if (oldSpeed == newSpeedMmps) {
                UpdateStabilizationState("速度采样无变化");
                return;
            }

            RealTimeSpeedMmps = newSpeedMmps;
            RaiseEventSafely(
                SpeedChanged,
                nameof(SpeedChanged),
                new LoopTrackSpeedChangedEventArgs {
                    OldRealTimeSpeedMmps = oldSpeed,
                    NewRealTimeSpeedMmps = newSpeedMmps,
                    TargetSpeedMmps = TargetSpeedMmps,
                    ChangedAt = DateTime.Now
                });

            UpdateStabilizationState("速度变化触发稳速评估");
        }

        /// <summary>
        /// 更新连接状态并触发事件。
        /// </summary>
        /// <param name="newStatus">新状态。</param>
        /// <param name="message">状态说明。</param>
        private void SetConnectionStatus(LoopTrackConnectionStatus newStatus, string? message = null) {
            LoopTrackConnectionStatus oldStatus;
            lock (_stateLock) {
                oldStatus = ConnectionStatus;
                if (oldStatus == newStatus) {
                    return;
                }

                ConnectionStatus = newStatus;
            }

            RaiseEventSafely(
                ConnectionStatusChanged,
                nameof(ConnectionStatusChanged),
                new LoopTrackConnectionStatusChangedEventArgs {
                    OldStatus = oldStatus,
                    NewStatus = newStatus,
                    ChangedAt = DateTime.Now,
                    Message = message
                });
        }

        /// <summary>
        /// 更新运行状态并触发事件。
        /// </summary>
        /// <param name="newStatus">新状态。</param>
        /// <param name="message">状态说明。</param>
        private void SetRunStatus(LoopTrackRunStatus newStatus, string? message = null) {
            LoopTrackRunStatus oldStatus;
            lock (_stateLock) {
                oldStatus = RunStatus;
                if (oldStatus == newStatus) {
                    return;
                }

                RunStatus = newStatus;
            }

            RaiseEventSafely(
                RunStatusChanged,
                nameof(RunStatusChanged),
                new LoopTrackRunStatusChangedEventArgs {
                    OldStatus = oldStatus,
                    NewStatus = newStatus,
                    ChangedAt = DateTime.Now,
                    Message = message
                });
        }

        /// <summary>
        /// 重置稳速状态并发布重置事件。
        /// </summary>
        /// <param name="reason">重置原因。</param>
        private void ResetStabilization(string reason) {
            _stabilizationStartedAt = null;
            StabilizationElapsed = null;
            SetStabilizationStatus(LoopTrackStabilizationStatus.NotStabilized, reason);

            RaiseEventSafely(
                StabilizationReset,
                nameof(StabilizationReset),
                new LoopTrackStabilizationResetEventArgs {
                    Reason = reason,
                    OccurredAt = DateTime.Now
                });
        }

        /// <summary>
        /// 更新稳速状态机。
        /// </summary>
        /// <param name="message">状态说明。</param>
        private void UpdateStabilizationState(string message) {
            // 步骤1：不满足稳速前置条件时直接重置稳速状态。
            if (TargetSpeedMmps <= 0m || RunStatus != LoopTrackRunStatus.Running) {
                ResetStabilization($"{message}：目标速度为零或未运行。");
                return;
            }

            var gap = Math.Abs(RealTimeSpeedMmps - TargetSpeedMmps);
            // 步骤2：速度偏差在容差内时推进稳速过程并累计稳速耗时。
            if (gap <= _stabilizedToleranceMmps) {
                if (_stabilizationStartedAt is null) {
                    _stabilizationStartedAt = DateTime.Now;
                    SetStabilizationStatus(LoopTrackStabilizationStatus.Stabilizing, $"{message}：进入稳速过程。");
                }
                else {
                    StabilizationElapsed = DateTime.Now - _stabilizationStartedAt.Value;
                    SetStabilizationStatus(LoopTrackStabilizationStatus.Stabilized, $"{message}：稳速达成。");
                }

                return;
            }

            // 步骤3：偏差超容差时发布未达速事件并回退到未稳速状态。
            if (_stabilizationStartedAt is not null) {
                RaiseEventSafely(
                    SpeedNotReached,
                    nameof(SpeedNotReached),
                    new LoopTrackSpeedNotReachedEventArgs {
                        TargetMmps = TargetSpeedMmps,
                        ActualMmps = RealTimeSpeedMmps,
                        TargetHz = LeiMaSpeedConverter.MmpsToHz(TargetSpeedMmps),
                        ActualHz = LeiMaSpeedConverter.MmpsToHz(RealTimeSpeedMmps),
                        IssuedHz = LeiMaSpeedConverter.MmpsToHz(TargetSpeedMmps),
                        GapHz = LeiMaSpeedConverter.MmpsToHz(gap),
                        LimitReason = "速度偏差超出稳速容差。",
                        OccurredAt = DateTime.Now
                    });
            }

            _stabilizationStartedAt = null;
            StabilizationElapsed = null;
            SetStabilizationStatus(LoopTrackStabilizationStatus.NotStabilized, $"{message}：尚未达到稳速容差。");
        }

        /// <summary>
        /// 设置稳速状态并触发事件。
        /// </summary>
        /// <param name="newStatus">新稳速状态。</param>
        /// <param name="message">状态说明。</param>
        private void SetStabilizationStatus(LoopTrackStabilizationStatus newStatus, string? message) {
            var oldStatus = StabilizationStatus;
            if (oldStatus == newStatus) {
                return;
            }

            StabilizationStatus = newStatus;
            RaiseEventSafely(
                StabilizationStatusChanged,
                nameof(StabilizationStatusChanged),
                new LoopTrackStabilizationStatusChangedEventArgs {
                    OldStatus = oldStatus,
                    NewStatus = newStatus,
                    StabilizationElapsed = StabilizationElapsed,
                    RealTimeSpeedMmps = RealTimeSpeedMmps,
                    TargetSpeedMmps = TargetSpeedMmps,
                    ChangedAt = DateTime.Now,
                    Message = message
                });
        }

        /// <summary>
        /// 安全发布故障事件。
        /// </summary>
        /// <param name="faultedEventArgs">故障载荷。</param>
        private void PublishFaultEvent(LoopTrackManagerFaultedEventArgs faultedEventArgs) {
            try {
                Faulted?.Invoke(this, faultedEventArgs);
            }
            catch {
            }
        }

        /// <summary>
        /// 安全触发事件，隔离事件回调异常。
        /// </summary>
        /// <typeparam name="TEventArgs">事件参数类型。</typeparam>
        /// <param name="eventHandler">事件委托。</param>
        /// <param name="eventName">事件名称。</param>
        /// <param name="args">事件参数。</param>
        private void RaiseEventSafely<TEventArgs>(
            EventHandler<TEventArgs>? eventHandler,
            string eventName,
            TEventArgs args)
            where TEventArgs : struct {
            if (eventHandler is null) {
                return;
            }

            foreach (var subscriber in eventHandler.GetInvocationList().Cast<EventHandler<TEventArgs>>()) {
                try {
                    subscriber(this, args);
                }
                catch (Exception ex) {
                    PublishFaultEvent(new LoopTrackManagerFaultedEventArgs {
                        Operation = $"LeiMa.EventCallback.{eventName}",
                        Exception = ex,
                        FaultedAt = DateTime.Now
                    });
                }
            }
        }

        /// <summary>
        /// 对已释放对象进行调用检查。
        /// </summary>
        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(LeiMaLoopTrackManager));
            }
        }
    }
}
