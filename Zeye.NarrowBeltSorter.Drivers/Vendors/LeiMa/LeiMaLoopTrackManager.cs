using Zeye.NarrowBeltSorter.Core.Enums.Track;
using Zeye.NarrowBeltSorter.Core.Events.Track;
using Zeye.NarrowBeltSorter.Core.Algorithms;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Utilities.LoopTrack;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa {
    /// <summary>
    /// 雷码 LM1000H 环形轨道管理器实现。
    /// </summary>
    public sealed class LeiMaLoopTrackManager : ILoopTrackManager {
        private readonly object _stateLock = new();
        private readonly ILeiMaModbusClientAdapter _modbusClient;
        private readonly SafeExecutor _safeExecutor;
        private readonly decimal _maxOutputHz;
        private readonly ushort _maxTorqueRawUnit;
        private readonly TimeSpan _pollingInterval;
        private readonly TimeSpan _torqueSetpointWriteInterval;
        private readonly decimal _stabilizedToleranceMmps;
        private readonly decimal _runningFrequencyLowThresholdHz;
        private readonly PidController _pidController;

        private CancellationTokenSource? _pollingCts;
        private Task? _pollingTask;
        private bool _disposed;
        private DateTime? _stabilizationStartedAt;
        private DateTime _lastTorqueSetpointWrittenAt;
        private PidControllerState _pidState;

        /// <summary>
        /// 初始化雷码环形轨道管理器。
        /// </summary>
        /// <param name="trackName">轨道名称。</param>
        /// <param name="modbusClient">Modbus 客户端。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <param name="connectionOptions">连接配置。</param>
        /// <param name="pidOptions">PID 配置。</param>
        /// <param name="maxOutputHz">最大输出频率（Hz）。</param>
        /// <param name="maxTorqueRawUnit">P3.10 转矩给定最大原始值。</param>
        /// <param name="pollingInterval">轮询周期。</param>
        /// <param name="torqueSetpointWriteInterval">P3.10 写入最小间隔。</param>
        /// <param name="stabilizedToleranceMmps">稳速判定容差（mm/s）。</param>
        /// <param name="runningFrequencyLowThresholdHz">低频告警阈值（Hz）。</param>
        public LeiMaLoopTrackManager(
            string trackName,
            ILeiMaModbusClientAdapter modbusClient,
            SafeExecutor safeExecutor,
            LoopTrackConnectionOptions? connectionOptions = null,
            LoopTrackPidOptions? pidOptions = null,
            decimal maxOutputHz = 25m,
            ushort maxTorqueRawUnit = 1000,
            TimeSpan? pollingInterval = null,
            TimeSpan? torqueSetpointWriteInterval = null,
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
            _torqueSetpointWriteInterval = torqueSetpointWriteInterval ?? _pollingInterval;
            _stabilizedToleranceMmps = Math.Max(0m, stabilizedToleranceMmps);
            _runningFrequencyLowThresholdHz = Math.Max(0m, runningFrequencyLowThresholdHz);

            ConnectionOptions = connectionOptions ?? new LoopTrackConnectionOptions();
            PidOptions = pidOptions ?? new LoopTrackPidOptions();
            ConnectionStatus = LoopTrackConnectionStatus.Disconnected;
            RunStatus = LoopTrackRunStatus.Stopped;
            StabilizationStatus = LoopTrackStabilizationStatus.NotStabilized;

            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _pidController = new PidController(new PidControllerOptions {
                Kp = PidOptions.Kp,
                Ki = PidOptions.Ki,
                Kd = PidOptions.Kd,
                SamplePeriodSeconds = (decimal)_pollingInterval.TotalSeconds,
                OutputMinHz = PidOptions.OutputMinHz,
                OutputMaxHz = Math.Min(_maxOutputHz, PidOptions.OutputMaxHz),
                IntegralMin = PidOptions.IntegralMin,
                IntegralMax = PidOptions.IntegralMax,
                DerivativeFilterAlpha = PidOptions.DerivativeFilterAlpha,
                MmpsPerHz = LeiMaSpeedConverter.MmpsPerHz
            });
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
        public async ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default) {
            ThrowIfDisposed();

            if (ConnectionStatus == LoopTrackConnectionStatus.Connected && _modbusClient.IsConnected) {
                return true;
            }

            // 步骤1：先切换到连接中状态，确保外部可观测到链路建立过程。
            SetConnectionStatus(LoopTrackConnectionStatus.Connecting, "正在连接雷码变频器。");

            // 步骤2：建立 Modbus 链路并至少读取一次运行状态/故障码验证链路有效性。
            var connected = await _safeExecutor.ExecuteAsync(
                async token => {
                    await _modbusClient.ConnectAsync(token).ConfigureAwait(false);
                    var runStatus = await _modbusClient.ReadHoldingRegisterAsync(LeiMaRegisters.RunStatus, token).ConfigureAwait(false);
                    var alarm = await _modbusClient.ReadHoldingRegisterAsync(LeiMaRegisters.AlarmCode, token).ConfigureAwait(false);
                    UpdateRunStatusFromRaw(alarm, runStatus, "连接完成状态同步");
                },
                "LeiMa.ConnectAsync",
                cancellationToken,
                ex => PublishFault("LeiMa.ConnectAsync", ex)).ConfigureAwait(false);

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
            // 步骤：先停止轮询任务再断开链路。
            var stopPollingTask = StopPollingLoopAsync();
            await stopPollingTask.ConfigureAwait(false);

            await _safeExecutor.ExecuteAsync(
                async token => {
                    if (_modbusClient.IsConnected) {
                        await _modbusClient.DisconnectAsync(token).ConfigureAwait(false);
                    }
                },
                "LeiMa.DisconnectAsync",
                cancellationToken,
                ex => PublishFault("LeiMa.DisconnectAsync", ex)).ConfigureAwait(false);

            SetConnectionStatus(LoopTrackConnectionStatus.Disconnected, "连接已断开。");
            SetRunStatus(LoopTrackRunStatus.Stopped, "断链后状态置停止。");
            ResetStabilization("断开连接重置稳速状态。");
            ResetPidState();
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

            var targetRawUnit = LeiMaSpeedConverter.HzToRawUnit(targetHz);
            if (targetHz > 0m && targetHz < _runningFrequencyLowThresholdHz) {
                RaiseEventSafely(
                    LowFrequencySetpointDetected,
                    nameof(LowFrequencySetpointDetected),
                    new LoopTrackLowFrequencySetpointEventArgs {
                        EstimatedMmps = normalized,
                        RawUnit = targetRawUnit,
                        TargetHz = targetHz,
                        ThresholdHz = _runningFrequencyLowThresholdHz,
                        OccurredAt = DateTime.Now
                    });
            }

            var written = await _safeExecutor.ExecuteAsync(
                token => _modbusClient.WriteSingleRegisterAsync(LeiMaRegisters.TorqueSetpoint, torqueRaw, token),
                "LeiMa.SetTargetSpeedAsync",
                cancellationToken,
                ex => PublishFault("LeiMa.SetTargetSpeedAsync", ex)).ConfigureAwait(false);

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

            var success = await _safeExecutor.ExecuteAsync(
                token => _modbusClient.WriteSingleRegisterAsync(
                    LeiMaRegisters.Command,
                    LeiMaRegisters.CommandForwardRun,
                    token),
                "LeiMa.StartAsync",
                cancellationToken,
                ex => PublishFault("LeiMa.StartAsync", ex)).ConfigureAwait(false);

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

            var success = await _safeExecutor.ExecuteAsync(
                token => _modbusClient.WriteSingleRegisterAsync(
                    LeiMaRegisters.Command,
                    LeiMaRegisters.CommandDecelerateStop,
                    token),
                "LeiMa.StopAsync",
                cancellationToken,
                ex => PublishFault("LeiMa.StopAsync", ex)).ConfigureAwait(false);

            if (success) {
                SetRunStatus(LoopTrackRunStatus.Stopped, "已下发减速停机命令。");
                ResetStabilization("停止命令触发稳速重置。");
                ResetPidState();
            }

            return success;
        }

        /// <inheritdoc />
        public async ValueTask<bool> ClearAlarmAsync(CancellationToken cancellationToken = default) {
            ThrowIfDisposed();
            if (ConnectionStatus != LoopTrackConnectionStatus.Connected || !_modbusClient.IsConnected) {
                return false;
            }

            // 步骤1：写入故障复位命令。
            var resetSuccess = await _safeExecutor.ExecuteAsync(
                token => _modbusClient.WriteSingleRegisterAsync(
                    LeiMaRegisters.Command,
                    LeiMaRegisters.CommandAlarmReset,
                    token),
                "LeiMa.ClearAlarmAsync.WriteResetCommand",
                cancellationToken,
                ex => PublishFault("LeiMa.ClearAlarmAsync.WriteResetCommand", ex)).ConfigureAwait(false);

            if (!resetSuccess) {
                return false;
            }

            // 步骤2：回读故障码确认复位结果。
            var (readSuccess, alarmCode) = await _safeExecutor.ExecuteAsync(
                token => _modbusClient.ReadHoldingRegisterAsync(LeiMaRegisters.AlarmCode, token),
                "LeiMa.ClearAlarmAsync.ReadBackAlarm",
                (ushort)ushort.MaxValue,
                cancellationToken,
                ex => PublishFault("LeiMa.ClearAlarmAsync.ReadBackAlarm", ex)).ConfigureAwait(false);

            if (!readSuccess) {
                return false;
            }

            // 步骤3：根据回读故障码更新运行状态。
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
            // 步骤：释放时先停轮询，再断开连接，最后释放底层客户端。
            var stopPollingTask = StopPollingLoopAsync();
            await stopPollingTask.ConfigureAwait(false);
            var disconnectTask = DisconnectAsync();
            await disconnectTask.ConfigureAwait(false);
            var disposeClientTask = _modbusClient.DisposeAsync();
            await disposeClientTask.ConfigureAwait(false);
        }

        /// <summary>
        /// 启动后台轮询任务。
        /// </summary>
        private void StartPollingLoop() {
            lock (_stateLock) {
                if (_pollingTask is not null) {
                    return;
                }

                var pollingCts = new CancellationTokenSource();
                _pollingCts = pollingCts;
                _pollingTask = Task.Run(() => PollingLoopAsync(pollingCts.Token));
            }
        }

        /// <summary>
        /// 停止后台轮询任务。
        /// </summary>
        private async Task StopPollingLoopAsync() {
            CancellationTokenSource? cts;
            Task? task;
            lock (_stateLock) {
                cts = _pollingCts;
                task = _pollingTask;
                _pollingCts = null;
                _pollingTask = null;
            }

            if (cts is null || task is null) {
                return;
            }

            try {
                cts.Cancel();
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // _logger.LogError 由 SafeExecutor 在其他异常路径统一记录。
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
                // 步骤：轮询循环逐次执行状态采样。
                var pollOnceTask = PollOnceAsync(cancellationToken);
                await pollOnceTask.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 执行一次状态采样轮询。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task PollOnceAsync(CancellationToken cancellationToken) {
            // 步骤1：采集运行状态、故障码与速度来源寄存器。
            var (runOk, runRaw) = await _safeExecutor.ExecuteAsync(
                token => _modbusClient.ReadHoldingRegisterAsync(LeiMaRegisters.RunStatus, token),
                "LeiMa.Poll.RunStatus",
                (ushort)3,
                cancellationToken,
                ex => PublishFault("LeiMa.Poll.RunStatus", ex)).ConfigureAwait(false);

            var (alarmOk, alarmRaw) = await _safeExecutor.ExecuteAsync(
                token => _modbusClient.ReadHoldingRegisterAsync(LeiMaRegisters.AlarmCode, token),
                "LeiMa.Poll.AlarmCode",
                (ushort)0,
                cancellationToken,
                ex => PublishFault("LeiMa.Poll.AlarmCode", ex)).ConfigureAwait(false);

            var (encoderOk, encoderRaw) = await _safeExecutor.ExecuteAsync(
                token => _modbusClient.ReadHoldingRegisterAsync(LeiMaRegisters.EncoderFeedbackSpeed, token),
                "LeiMa.Poll.EncoderSpeed",
                (ushort)0,
                cancellationToken,
                ex => PublishFault("LeiMa.Poll.EncoderSpeed", ex)).ConfigureAwait(false);

            var (runningFreqOk, runningFreqRaw) = await _safeExecutor.ExecuteAsync(
                token => _modbusClient.ReadHoldingRegisterAsync(LeiMaRegisters.RunningFrequency, token),
                "LeiMa.Poll.RunningFrequency",
                (ushort)0,
                cancellationToken,
                ex => PublishFault("LeiMa.Poll.RunningFrequency", ex)).ConfigureAwait(false);

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

            if (runOk && alarmOk) {
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
            await ExecutePidClosedLoopAsync(speedMmps, cancellationToken).ConfigureAwait(false);
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
        /// 执行一次 PID 闭环稳速并写入 P3.10。
        /// </summary>
        /// <param name="realTimeSpeedMmps">当前反馈速度（mm/s）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task ExecutePidClosedLoopAsync(decimal realTimeSpeedMmps, CancellationToken cancellationToken) {
            // 步骤1：校验闭环前置条件，确保连接与运行状态有效。
            if (!PidOptions.Enabled || !_modbusClient.IsConnected || ConnectionStatus != LoopTrackConnectionStatus.Connected) {
                return;
            }

            if (RunStatus != LoopTrackRunStatus.Running) {
                ResetPidState();
                return;
            }

            if (TargetSpeedMmps <= 0m) {
                ResetPidState();
                return;
            }

            // 步骤2：执行 PID 计算并更新快照状态，供调参日志使用。
            var input = new PidControllerInput(TargetSpeedMmps, realTimeSpeedMmps, false);
            var output = _pidController.Compute(input, _pidState);
            _pidState = output.NextState;
            PidLastUpdatedAt = DateTime.Now;
            PidLastErrorMmps = output.ErrorSpeedMmps;
            PidLastProportionalHz = output.Proportional;
            PidLastIntegralHz = output.Integral;
            PidLastDerivativeHz = output.Derivative;
            PidLastUnclampedHz = output.UnclampedHz;
            PidLastCommandHz = output.CommandHz;
            PidLastOutputClamped = output.OutputClamped;

            var now = DateTime.Now;
            var commandMmps = LeiMaSpeedConverter.HzToMmps(output.CommandHz);
            var torqueRaw = LeiMaSpeedConverter.MmpsToTorqueRawUnit(commandMmps, _maxOutputHz, _maxTorqueRawUnit);
            var shouldWriteByInterval = now - _lastTorqueSetpointWrittenAt >= _torqueSetpointWriteInterval;
            if (!shouldWriteByInterval) {
                return;
            }

            // 步骤3：按节流策略写入 P3.10，并在限幅场景发布事件。
            var writeSuccess = await _safeExecutor.ExecuteAsync(
                token => _modbusClient.WriteSingleRegisterAsync(LeiMaRegisters.TorqueSetpoint, torqueRaw, token),
                "LeiMa.PidClosedLoop.WriteTorqueSetpoint",
                cancellationToken,
                ex => PublishFault("LeiMa.PidClosedLoop.WriteTorqueSetpoint", ex)).ConfigureAwait(false);

            if (!writeSuccess) {
                return;
            }

            _lastTorqueSetpointWrittenAt = now;

            if (output.OutputClamped) {
                RaiseEventSafely(
                    FrequencySetpointHardClamped,
                    nameof(FrequencySetpointHardClamped),
                    new LoopTrackFrequencySetpointHardClampedEventArgs {
                        RequestedRawUnit = LeiMaSpeedConverter.HzToRawUnit(output.UnclampedHz),
                        RequestedHz = output.UnclampedHz,
                        ClampMaxHz = _maxOutputHz,
                        ClampedRawUnit = LeiMaSpeedConverter.HzToRawUnit(output.CommandHz),
                        OccurredAt = now
                    });
            }
        }

        /// <summary>
        /// 重置 PID 运行状态。
        /// </summary>
        private void ResetPidState() {
            _pidState = new PidControllerState(0m, 0m, 0m, false);
            PidLastUpdatedAt = null;
            PidLastErrorMmps = 0m;
            PidLastProportionalHz = 0m;
            PidLastIntegralHz = 0m;
            PidLastDerivativeHz = 0m;
            PidLastUnclampedHz = 0m;
            PidLastCommandHz = 0m;
            PidLastOutputClamped = false;
            _lastTorqueSetpointWrittenAt = DateTime.MinValue;
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
        /// 发布故障事件载荷。
        /// </summary>
        /// <param name="operation">故障操作名称。</param>
        /// <param name="exception">异常对象。</param>
        private void PublishFault(string operation, Exception exception) {
            PublishFaultEvent(new LoopTrackManagerFaultedEventArgs {
                Operation = operation,
                Exception = exception,
                FaultedAt = DateTime.Now
            });
        }

        /// <summary>
        /// 安全发布故障事件。
        /// </summary>
        /// <param name="faultedEventArgs">故障载荷。</param>
        private void PublishFaultEvent(LoopTrackManagerFaultedEventArgs faultedEventArgs) {
            _safeExecutor.Execute(
                () => Faulted?.Invoke(this, faultedEventArgs),
                "LeiMa.PublishFaultEvent");
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
                    // _logger.LogError 由 SafeExecutor 在故障发布链路统一记录。
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
