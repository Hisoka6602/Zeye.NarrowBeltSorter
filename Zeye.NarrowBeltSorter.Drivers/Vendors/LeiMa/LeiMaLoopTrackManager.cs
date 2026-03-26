using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Enums.Track;
using Zeye.NarrowBeltSorter.Core.Events.Track;
using Zeye.NarrowBeltSorter.Core.Algorithms;
using Zeye.NarrowBeltSorter.Core.Options.Pid;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Utilities.LoopTrack;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa {
    /// <summary>
    /// 雷码 LM1000H 环形轨道管理器实现。
    /// </summary>
    public sealed class LeiMaLoopTrackManager : ILoopTrackManager {
        private static readonly Logger DebugLogger = LogManager.GetLogger(nameof(LeiMaLoopTrackManager));
        private static int _loopTrackDebugLogConfigured;
        private readonly object _stateLock = new();
        private readonly ILeiMaModbusClientAdapter _modbusClient;
        private readonly IReadOnlyList<(byte SlaveAddress, ILeiMaModbusClientAdapter Adapter)> _slaveClients;
        private readonly SpeedAggregateStrategy _speedAggregateStrategy;
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
            decimal runningFrequencyLowThresholdHz = 0.5m,
            IReadOnlyList<(byte SlaveAddress, ILeiMaModbusClientAdapter Adapter)>? slaveClients = null,
            SpeedAggregateStrategy speedAggregateStrategy = SpeedAggregateStrategy.Min) {
            if (string.IsNullOrWhiteSpace(trackName)) {
                throw new ArgumentException("轨道名称不能为空。", nameof(trackName));
            }

            if (maxOutputHz <= 0m) {
                throw new ArgumentOutOfRangeException(nameof(maxOutputHz), "最大输出频率必须大于 0。");
            }

            TrackName = trackName;
            _modbusClient = modbusClient ?? throw new ArgumentNullException(nameof(modbusClient));
            _slaveClients = BuildSlaveClients(modbusClient, slaveClients);
            _speedAggregateStrategy = speedAggregateStrategy;
            _maxOutputHz = maxOutputHz;
            _maxTorqueRawUnit = maxTorqueRawUnit;
            _pollingInterval = pollingInterval ?? TimeSpan.FromMilliseconds(300);
            _torqueSetpointWriteInterval = torqueSetpointWriteInterval ?? _pollingInterval;
            _stabilizedToleranceMmps = Math.Max(0m, stabilizedToleranceMmps);
            _runningFrequencyLowThresholdHz = Math.Max(0m, runningFrequencyLowThresholdHz);
            EnsureLoopTrackDebugLoggingConfigured();

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
            }, NullLogger.Instance);
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

            if (ConnectionStatus == LoopTrackConnectionStatus.Connected && _slaveClients.All(x => x.Adapter.IsConnected)) {
                return true;
            }

            // 步骤1：先切换到连接中状态，确保外部可观测到链路建立过程。
            SetConnectionStatus(LoopTrackConnectionStatus.Connecting, "正在连接雷码变频器。");

            // 步骤2：建立 Modbus 链路并至少读取一次运行状态/故障码验证链路有效性。
            var operationId = CreateOperationId();
            var connected = await _safeExecutor.ExecuteAsync(
                async token => {
                    foreach (var (_, slaveAdapter) in _slaveClients) {
                        await slaveAdapter.ConnectAsync(token).ConfigureAwait(false);
                    }

                    var (_, primaryAdapter) = _slaveClients[0];
                    var runStatus = await primaryAdapter.ReadHoldingRegisterAsync(LeiMaRegisters.RunStatus, token).ConfigureAwait(false);
                    var alarm = await primaryAdapter.ReadHoldingRegisterAsync(LeiMaRegisters.AlarmCode, token).ConfigureAwait(false);
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
            DebugLogger.Info("LoopTrack连接成功 operationId={0} slaves={1}", operationId, string.Join(",", _slaveClients.Select(x => x.SlaveAddress)));
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
                    foreach (var (_, adapter) in _slaveClients) {
                        if (adapter.IsConnected) {
                            await adapter.DisconnectAsync(token).ConfigureAwait(false);
                        }
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

            if (ConnectionStatus != LoopTrackConnectionStatus.Connected || !AreAllSlavesConnected()) {
                return false;
            }

            var operationId = CreateOperationId();
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

            var (written, failedSlaves) = await WriteRegisterToAllSlavesAsync(
                LeiMaRegisters.TorqueSetpoint,
                torqueRaw,
                "LeiMa.SetTargetSpeedAsync",
                cancellationToken).ConfigureAwait(false);

            if (!written) {
                DebugLogger.Warn("LoopTrack设速失败 operationId={0} requestMmps={1} limitedMmps={2} failedSlaves={3} 建议=检查从站地址冲突/串口占用/终端电阻", operationId, speedMmps, normalized, string.Join(",", failedSlaves));
                return false;
            }

            DebugLogger.Info("LoopTrack设速成功 operationId={0} requestMmps={1} limitedMmps={2} slaves={3} failedSlaves=", operationId, speedMmps, normalized, string.Join(",", _slaveClients.Select(x => x.SlaveAddress)));
            TargetSpeedMmps = normalized;
            UpdateStabilizationState("目标速度变更");
            return true;
        }

        /// <inheritdoc />
        public async ValueTask<bool> StartAsync(CancellationToken cancellationToken = default) {
            ThrowIfDisposed();
            if (ConnectionStatus != LoopTrackConnectionStatus.Connected || !AreAllSlavesConnected()) {
                return false;
            }

            var operationId = CreateOperationId();
            var (success, failedSlaves) = await WriteRegisterToAllSlavesAsync(
                LeiMaRegisters.Command,
                LeiMaRegisters.CommandForwardRun,
                "LeiMa.StartAsync",
                cancellationToken).ConfigureAwait(false);

            if (success) {
                SetRunStatus(LoopTrackRunStatus.Running, "已下发正转运行命令。");
                DebugLogger.Info("LoopTrack启动成功 operationId={0} slaves={1}", operationId, string.Join(",", _slaveClients.Select(x => x.SlaveAddress)));
            }
            else {
                DebugLogger.Warn("LoopTrack启动失败 operationId={0} failedSlaves={1} 建议=检查从站地址冲突/串口占用/终端电阻", operationId, string.Join(",", failedSlaves));
            }

            return success;
        }

        /// <inheritdoc />
        public async ValueTask<bool> StopAsync(CancellationToken cancellationToken = default) {
            ThrowIfDisposed();
            if (ConnectionStatus != LoopTrackConnectionStatus.Connected || !AreAllSlavesConnected()) {
                return false;
            }

            var operationId = CreateOperationId();
            var (success, failedSlaves) = await WriteRegisterToAllSlavesAsync(
                LeiMaRegisters.Command,
                LeiMaRegisters.CommandDecelerateStop,
                "LeiMa.StopAsync",
                cancellationToken).ConfigureAwait(false);

            if (success) {
                SetRunStatus(LoopTrackRunStatus.Stopped, "已下发减速停机命令。");
                ResetStabilization("停止命令触发稳速重置。");
                ResetPidState();
                DebugLogger.Info("LoopTrack停机成功 operationId={0} slaves={1}", operationId, string.Join(",", _slaveClients.Select(x => x.SlaveAddress)));
            }
            else {
                DebugLogger.Warn("LoopTrack停机失败 operationId={0} failedSlaves={1} 建议=检查从站地址冲突/串口占用/终端电阻", operationId, string.Join(",", failedSlaves));
            }

            return success;
        }

        /// <inheritdoc />
        public async ValueTask<bool> ClearAlarmAsync(CancellationToken cancellationToken = default) {
            ThrowIfDisposed();
            if (ConnectionStatus != LoopTrackConnectionStatus.Connected || !AreAllSlavesConnected()) {
                return false;
            }

            // 步骤1：向全部从站广播故障复位命令。
            var (resetSuccess, _) = await WriteRegisterToAllSlavesAsync(
                LeiMaRegisters.Command,
                LeiMaRegisters.CommandAlarmReset,
                "LeiMa.ClearAlarmAsync.WriteResetCommand",
                cancellationToken).ConfigureAwait(false);
            if (!resetSuccess) {
                return false;
            }

            // 步骤2：回读全部从站故障码确认复位结果。
            var hasAlarm = false;
            foreach (var (slaveAddress, adapter) in _slaveClients) {
                var (readSuccess, alarmCode) = await _safeExecutor.ExecuteAsync(
                    token => adapter.ReadHoldingRegisterAsync(LeiMaRegisters.AlarmCode, token),
                    $"LeiMa.ClearAlarmAsync.ReadBackAlarm.Slave{slaveAddress}",
                    (ushort)ushort.MaxValue,
                    cancellationToken,
                    ex => PublishFault($"LeiMa.ClearAlarmAsync.ReadBackAlarm.Slave{slaveAddress}", ex)).ConfigureAwait(false);
                if (!readSuccess) {
                    return false;
                }

                if (alarmCode != 0) {
                    hasAlarm = true;
                }
            }

            // 步骤3：根据全部从站回读故障码更新运行状态。
            if (hasAlarm) {
                SetRunStatus(LoopTrackRunStatus.Faulted, "故障未清除，存在从站故障码非零。");
                return false;
            }

            SetRunStatus(LoopTrackRunStatus.Stopped, "故障复位完成。");
            return true;
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
            foreach (var (_, adapter) in _slaveClients.DistinctBy(x => x.Adapter)) {
                var disposeClientTask = adapter.DisposeAsync();
                await disposeClientTask.ConfigureAwait(false);
            }
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
            var operationId = CreateOperationId();
            var (_, primaryAdapter) = _slaveClients[0];
            var (runOk, runRaw) = await _safeExecutor.ExecuteAsync(
                token => primaryAdapter.ReadHoldingRegisterAsync(LeiMaRegisters.RunStatus, token),
                "LeiMa.Poll.RunStatus",
                (ushort)3,
                cancellationToken,
                ex => PublishFault("LeiMa.Poll.RunStatus", ex)).ConfigureAwait(false);

            var (alarmOk, alarmRaw) = await _safeExecutor.ExecuteAsync(
                token => primaryAdapter.ReadHoldingRegisterAsync(LeiMaRegisters.AlarmCode, token),
                "LeiMa.Poll.AlarmCode",
                (ushort)0,
                cancellationToken,
                ex => PublishFault("LeiMa.Poll.AlarmCode", ex)).ConfigureAwait(false);

            var samples = new List<(byte SlaveId, decimal Mmps)>(_slaveClients.Count);
            var failedSlaves = new List<byte>();
            foreach (var (slaveAddress, adapter) in _slaveClients) {
                var (sampleOk, sampleRaw) = await _safeExecutor.ExecuteAsync(
                    token => adapter.ReadHoldingRegisterAsync(LeiMaRegisters.EncoderFeedbackSpeed, token),
                    $"LeiMa.Poll.EncoderSpeed.Slave{slaveAddress}",
                    (ushort)0,
                    cancellationToken,
                    ex => PublishFault($"LeiMa.Poll.EncoderSpeed.Slave{slaveAddress}", ex)).ConfigureAwait(false);
                if (!sampleOk) {
                    failedSlaves.Add(slaveAddress);
                    continue;
                }

                samples.Add((slaveAddress, LeiMaSpeedConverter.HzToMmps(LeiMaSpeedConverter.RawUnitToHz(sampleRaw))));
            }

            var totalSlavesCount = _slaveClients.Count;
            if (samples.Count > 0 && samples.Count < totalSlavesCount) {
                RaiseEventSafely(
                    SpeedSamplingPartiallyFailed,
                    nameof(SpeedSamplingPartiallyFailed),
                    new LoopTrackSpeedSamplingPartiallyFailedEventArgs {
                        SuccessCount = samples.Count,
                        FailCount = failedSlaves.Count,
                        FailedSlaveIds = string.Join(",", failedSlaves),
                        OccurredAt = DateTime.Now
                    });
                DebugLogger.Warn("LoopTrack采样部分失败 operationId={0} successCount={1} failCount={2} failedSlaves={3} 建议=检查从站地址冲突/串口占用/终端电阻", operationId, samples.Count, failedSlaves.Count, string.Join(",", failedSlaves));
            }

            if (runOk && alarmOk) {
                UpdateRunStatusFromRaw(alarmRaw, runRaw, "轮询状态同步");
            }

            if (samples.Count <= 0) {
                DebugLogger.Warn("LoopTrack采样全部失败 operationId={0} failedSlaves={1} 建议=检查从站地址冲突/串口占用/终端电阻", operationId, string.Join(",", failedSlaves));
                return;
            }

            // 步骤2：按策略汇总多从站速度并同步实时速度状态。
            var speedMmps = AggregateSpeed(samples, _speedAggregateStrategy);
            var spreadMmps = samples.Max(x => x.Mmps) - samples.Min(x => x.Mmps);
            if (spreadMmps > _stabilizedToleranceMmps) {
                RaiseEventSafely(
                    SpeedSpreadTooLargeDetected,
                    nameof(SpeedSpreadTooLargeDetected),
                    new LoopTrackSpeedSpreadTooLargeEventArgs {
                        Strategy = _speedAggregateStrategy,
                        SpreadMmps = spreadMmps,
                        Samples = string.Join(";", samples.Select(x => $"Slave{x.SlaveId}={x.Mmps:F2}")),
                        OccurredAt = DateTime.Now
                    });
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
            if (!PidOptions.Enabled || !AreAllSlavesConnected() || ConnectionStatus != LoopTrackConnectionStatus.Connected) {
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
            var (writeSuccess, failedSlaves) = await WriteRegisterToAllSlavesAsync(
                LeiMaRegisters.TorqueSetpoint,
                torqueRaw,
                "LeiMa.PidClosedLoop.WriteTorqueSetpoint",
                cancellationToken).ConfigureAwait(false);

            if (!writeSuccess) {
                DebugLogger.Warn("LoopTrack闭环写入失败 operationId={0} failedSlaves={1} 建议=检查从站地址冲突/串口占用/终端电阻", CreateOperationId(), string.Join(",", failedSlaves));
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

        /// <summary>
        /// 构建从站客户端集合，缺省回退到单客户端。
        /// </summary>
        /// <param name="defaultClient">默认客户端。</param>
        /// <param name="slaveClients">外部注入客户端集合。</param>
        /// <returns>从站客户端集合。</returns>
        private static IReadOnlyList<(byte SlaveAddress, ILeiMaModbusClientAdapter Adapter)> BuildSlaveClients(
            ILeiMaModbusClientAdapter defaultClient,
            IReadOnlyList<(byte SlaveAddress, ILeiMaModbusClientAdapter Adapter)>? slaveClients) {
            if (slaveClients is null || slaveClients.Count == 0) {
                return [(1, defaultClient)];
            }

            return slaveClients;
        }

        /// <summary>
        /// 配置 LoopTrack 调试文件日志（按天滚动）。
        /// </summary>
        public static void EnsureLoopTrackDebugLoggingConfigured() {
            if (Interlocked.CompareExchange(ref _loopTrackDebugLogConfigured, 1, 0) != 0) {
                return;
            }

            var configuration = LogManager.Configuration ?? new LoggingConfiguration();
            if (configuration.FindTargetByName("looptrackDebugFile") is not FileTarget) {
                var fileTarget = new FileTarget("looptrackDebugFile") {
                    FileName = "logs/looptrack-debug-${shortdate}.log",
                    Layout = "${longdate}|${level:uppercase=true}|${logger}|${message} ${exception:format=tostring}",
                    KeepFileOpen = true,
                    ConcurrentWrites = false,
                    ArchiveNumbering = ArchiveNumberingMode.Date,
                    ArchiveEvery = FileArchivePeriod.Day
                };

                configuration.AddTarget(fileTarget);
                configuration.LoggingRules.Add(new LoggingRule("*LoopTrackManagerService*", NLog.LogLevel.Debug, fileTarget));
                configuration.LoggingRules.Add(new LoggingRule("*LoopTrackHILWorker*", NLog.LogLevel.Debug, fileTarget));
                configuration.LoggingRules.Add(new LoggingRule("*LeiMaLoopTrackManager*", NLog.LogLevel.Debug, fileTarget));
                configuration.LoggingRules.Add(new LoggingRule("*LeiMaModbusClientAdapter*", NLog.LogLevel.Debug, fileTarget));
                LogManager.Configuration = configuration;
                LogManager.ReconfigExistingLoggers();
            }
        }

        /// <summary>
        /// 按策略汇总速度采样值。
        /// </summary>
        /// <param name="samples">采样列表。</param>
        /// <param name="strategy">汇总策略。</param>
        /// <returns>汇总速度。</returns>
        private static decimal AggregateSpeed(IReadOnlyList<(byte SlaveId, decimal Mmps)> samples, SpeedAggregateStrategy strategy) {
            if (samples.Count == 0) {
                return 0m;
            }

            return strategy switch {
                SpeedAggregateStrategy.Avg => samples.Average(x => x.Mmps),
                SpeedAggregateStrategy.Median => CalculateMedian(samples.Select(x => x.Mmps).ToList()),
                _ => samples.Min(x => x.Mmps)
            };
        }

        /// <summary>
        /// 计算中位数。
        /// </summary>
        /// <param name="values">速度集合。</param>
        /// <returns>中位数值。</returns>
        private static decimal CalculateMedian(List<decimal> values) {
            values.Sort();
            var count = values.Count;
            var middle = count / 2;
            if (count % 2 == 1) {
                return values[middle];
            }

            return (values[middle - 1] + values[middle]) / 2m;
        }

        /// <summary>
        /// 向所有从站广播写入并汇总失败从站。
        /// </summary>
        /// <param name="register">寄存器地址。</param>
        /// <param name="value">写入值。</param>
        /// <param name="operation">操作名称。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否全部成功及失败从站集合。</returns>
        private async ValueTask<(bool Success, List<byte> FailedSlaves)> WriteRegisterToAllSlavesAsync(
            ushort register,
            ushort value,
            string operation,
            CancellationToken cancellationToken) {
            var failedSlaves = new List<byte>();
            foreach (var (slaveAddress, adapter) in _slaveClients) {
                var writeSuccess = await _safeExecutor.ExecuteAsync(
                    token => adapter.WriteSingleRegisterAsync(register, value, token),
                    $"{operation}.Slave{slaveAddress}",
                    cancellationToken,
                    ex => PublishFault($"{operation}.Slave{slaveAddress}", ex)).ConfigureAwait(false);
                if (!writeSuccess) {
                    failedSlaves.Add(slaveAddress);
                }
            }

            return (failedSlaves.Count == 0, failedSlaves);
        }

        /// <summary>
        /// 判断全部从站是否已连接。
        /// </summary>
        /// <returns>全部从站已连接返回 true。</returns>
        private bool AreAllSlavesConnected() {
            return _slaveClients.All(x => x.Adapter.IsConnected);
        }

        /// <summary>
        /// 生成短格式操作编号。
        /// </summary>
        /// <returns>操作编号。</returns>
        private static string CreateOperationId() {
            return Guid.NewGuid().ToString("N")[..8];
        }
    }
}
