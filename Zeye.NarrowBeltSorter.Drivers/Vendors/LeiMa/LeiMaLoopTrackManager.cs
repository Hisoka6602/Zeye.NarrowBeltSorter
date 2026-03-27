using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Algorithms;
using Zeye.NarrowBeltSorter.Core.Enums.Track;
using Zeye.NarrowBeltSorter.Core.Options.Pid;
using Zeye.NarrowBeltSorter.Core.Events.Track;
using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Utilities.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa {

    /// <summary>
    /// 雷码 LM1000H 环形轨道管理器实现。
    /// </summary>
    public sealed class LeiMaLoopTrackManager : ILoopTrackManager {
        private static readonly NLog.Logger SpeedLogger = NLog.LogManager.GetLogger("looptrack-speed");
        private static readonly NLog.Logger DebugLogger = NLog.LogManager.GetLogger(nameof(LeiMaLoopTrackManager));
        private const byte DefaultSlaveAddress = 1;
        private readonly object _stateLock = new();
        private readonly ILeiMaModbusClientAdapter _modbusClient;
        private readonly IReadOnlyList<(byte SlaveAddress, ILeiMaModbusClientAdapter Adapter)> _slaveClients;
        private readonly SpeedAggregateStrategy _speedAggregateStrategy;
        private readonly SafeExecutor _safeExecutor;
        private readonly decimal _maxOutputHz;
        private readonly ushort _maxTorqueRawUnit;
        private decimal _torqueNormalizationTopHz;
        private readonly TimeSpan _pollingInterval;
        private readonly TimeSpan _torqueSetpointWriteInterval;
        private readonly TimeSpan _stabilizationWindow;
        private readonly decimal _stabilizedToleranceMmps;
        private readonly decimal _runningFrequencyLowThresholdHz;
        private readonly PidController _pidController;
        private static readonly TimeSpan IdleStatusPollingInterval = TimeSpan.FromSeconds(1);
        private CancellationTokenSource? _pollingCts;
        private Task? _pollingTask;
        private bool _disposed;
        private DateTime? _stabilizationStartedAt;
        private DateTime _lastTorqueSetpointWrittenAt;
        private PidControllerState _pidState;
        private DateTime _nextIdleStatusPollAt = DateTime.MinValue;
        private DateTime _pidStartupOpenLoopUntil = DateTime.MinValue;
        private ushort? _lastPidTorqueSetpointRaw;
        private static readonly TimeSpan PidStartupOpenLoopWindow = TimeSpan.FromSeconds(3);
        private readonly SemaphoreSlim _comIoGate = new(1, 1);
        private const decimal TorqueLaunchBoostKeepMaxRatio = 0.15m;
        private const decimal TorqueLaunchBoostFadeOutRatio = 0.60m;
        private const decimal TorqueLaunchBoostMinFloorRatio = 0.55m;

        /// <summary>
        /// 初始化雷码环形轨道管理器。
        /// </summary>
        /// <param name="trackName">轨道名称。</param>
        /// <param name="modbusClient">Modbus 客户端。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <param name="connectionOptions">连接配置。</param>
        /// <param name="pidOptions">PID 配置。</param>
        /// <param name="maxOutputHz">最大输出频率（Hz，仅限幅）。</param>
        /// <param name="maxTorqueRawUnit">P3.10 转矩给定最大原始值。</param>
        /// <param name="pollingInterval">轮询周期。</param>
        /// <param name="torqueSetpointWriteInterval">P3.10 写入最小间隔。</param>
        /// <param name="stabilizedToleranceMmps">稳速判定容差（mm/s）。</param>
        /// <param name="stabilizationWindow">稳速判定窗口。</param>
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
            TimeSpan? stabilizationWindow = null,
            decimal runningFrequencyLowThresholdHz = 0.5m,
            IReadOnlyList<(byte SlaveAddress, ILeiMaModbusClientAdapter Adapter)>? slaveClients = null,
            SpeedAggregateStrategy speedAggregateStrategy = SpeedAggregateStrategy.Min) {
            if (string.IsNullOrWhiteSpace(trackName)) {
                throw new ArgumentException("轨道名称不能为空。", nameof(trackName));
            }

            if (maxOutputHz <= 0m) {
                throw new ArgumentOutOfRangeException(nameof(maxOutputHz), "最大输出频率必须大于 0。");
            }
            if (stabilizationWindow is not null && stabilizationWindow.Value <= TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(stabilizationWindow), "稳速判定窗口必须大于 0。");
            }

            TrackName = trackName;
            _modbusClient = modbusClient ?? throw new ArgumentNullException(nameof(modbusClient));
            _slaveClients = BuildSlaveClients(modbusClient, connectionOptions, slaveClients);
            _speedAggregateStrategy = speedAggregateStrategy;
            _maxOutputHz = maxOutputHz;
            _maxTorqueRawUnit = maxTorqueRawUnit;
            _torqueNormalizationTopHz = Math.Min(_maxOutputHz, 50m);
            _pollingInterval = pollingInterval ?? TimeSpan.FromMilliseconds(300);
            _torqueSetpointWriteInterval = torqueSetpointWriteInterval ?? _pollingInterval;
            _stabilizationWindow = stabilizationWindow ?? TimeSpan.FromMilliseconds(500);
            _stabilizedToleranceMmps = Math.Max(0m, stabilizedToleranceMmps);
            _runningFrequencyLowThresholdHz = Math.Max(0m, runningFrequencyLowThresholdHz);
            ConnectionOptions = connectionOptions ?? new LoopTrackConnectionOptions();
            PidOptions = pidOptions ?? new LoopTrackPidOptions();
            ConnectionStatus = LoopTrackConnectionStatus.Disconnected;
            RunStatus = LoopTrackRunStatus.Stopped;
            StabilizationStatus = LoopTrackStabilizationStatus.NotStabilized;
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            var torqueOutputMaxRaw = Convert.ToDecimal(_maxTorqueRawUnit);
            var pidOutputMinRaw = Math.Clamp(PidOptions.OutputMinRaw, 0m, torqueOutputMaxRaw);
            var pidOutputMaxRaw = Math.Clamp(PidOptions.OutputMaxRaw, pidOutputMinRaw, torqueOutputMaxRaw);
            _pidController = new PidController(new PidControllerOptions {
                Kp = PidOptions.Kp,
                Ki = PidOptions.Ki,
                Kd = PidOptions.Kd,
                SamplePeriodSeconds = (decimal)_pollingInterval.TotalSeconds,

                OutputMinRaw = pidOutputMinRaw,
                OutputMaxRaw = pidOutputMaxRaw,
                IntegralMin = PidOptions.IntegralMin,
                IntegralMax = PidOptions.IntegralMax,
                DerivativeFilterAlpha = PidOptions.DerivativeFilterAlpha,
                ErrorScale = 1m
            }, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            var configuredSlaveAddresses = string.Join(",", _slaveClients.Select(x => x.SlaveAddress));
            DebugLogger.Info("Modbus从站配置 TrackName={0} SlaveCount={1} SlaveAddresses={2} SpeedAggregateStrategy={3}",
                TrackName, _slaveClients.Count, configuredSlaveAddresses, _speedAggregateStrategy);
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
        public decimal PidLastUnclampedOutput { get; private set; }

        /// <inheritdoc />
        public decimal PidLastCommandOutput { get; private set; }

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

            // 步骤2：先建立所有从站 Modbus 链路；状态同步改为非阻断式，避免单从站超时拖垮整体连接。
            var operationId = CreateOperationId();
            var connected = await _safeExecutor.ExecuteAsync(
                async token => {
                    foreach (var (_, slaveAdapter) in _slaveClients) {
                        await slaveAdapter.ConnectAsync(token).ConfigureAwait(false);
                        if (slaveAdapter.IsConnected) {
                            continue;
                        }
                    }
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
            await TryInitializeDriveParametersAsync(cancellationToken).ConfigureAwait(false);
            await RefreshTorqueNormalizationTopHzAsync(cancellationToken).ConfigureAwait(false);
            await TrySyncRunStatusAfterConnectAsync(cancellationToken).ConfigureAwait(false);
            DebugLogger.Info("LoopTrack连接成功 operationId={0} slaves={1}", operationId, string.Join(",", _slaveClients.Select(x => x.SlaveAddress)));
            StartPollingLoop();
            return true;
        }

        /// <summary>
        /// 连接后初始化关键参数：按 ZakYip 启动顺序执行初始化。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        private async ValueTask TryInitializeDriveParametersAsync(CancellationToken cancellationToken) {
            foreach (var (slaveAddress, adapter) in _slaveClients) {
                // 步骤1：下发减速停止指令，确保启动前驱动器处于停止态。
                _ = await _safeExecutor.ExecuteAsync(
                    token => ExecuteComSerializedAsync(innerToken => adapter.WriteSingleRegisterAsync(LeiMaRegisters.Command, LeiMaRegisters.CommandDecelerateStop, innerToken), token),
                    $"LeiMa.ConnectAsync.Init.CommandDecelerateStop.Slave{slaveAddress}",
                    cancellationToken,
                    ex => PublishFault($"LeiMa.ConnectAsync.Init.CommandDecelerateStop.Slave{slaveAddress}", ex)).ConfigureAwait(false);

                // 步骤2：设置运行指令源为 RS485，强制闭环控制链路。
                _ = await _safeExecutor.ExecuteAsync(
                    token => ExecuteComSerializedAsync(innerToken => adapter.WriteSingleRegisterAsync(LeiMaRegisters.RunCommandSource, LeiMaRegisters.RunCommandSourceRs485, innerToken), token),
                    $"LeiMa.ConnectAsync.Init.RunCommandSource.Slave{slaveAddress}",
                    cancellationToken,
                    ex => PublishFault($"LeiMa.ConnectAsync.Init.RunCommandSource.Slave{slaveAddress}", ex)).ConfigureAwait(false);

                // 步骤3：预置 P3.10 为最大转矩，确保启动时有足够驱动力。
                _ = await _safeExecutor.ExecuteAsync(
                    token => ExecuteComSerializedAsync(innerToken => adapter.WriteSingleRegisterAsync(LeiMaRegisters.TorqueSetpoint, _maxTorqueRawUnit, innerToken), token),
                    $"LeiMa.ConnectAsync.Init.TorqueSetpointMax.Slave{slaveAddress}",
                    cancellationToken,
                ex => PublishFault($"LeiMa.ConnectAsync.Init.TorqueSetpointMax.Slave{slaveAddress}", ex)).ConfigureAwait(false);

                // 步骤4：读取基准频率（P0.05）与额定电流（P2.06）用于诊断日志。
                var (baseFrequencyOk, baseFrequencyRaw) = await _safeExecutor.ExecuteAsync(
                    token => ExecuteComSerializedAsync(innerToken => adapter.ReadHoldingRegisterAsync(LeiMaRegisters.BaseFrequency, innerToken), token),
                    $"LeiMa.ConnectAsync.Init.ReadBaseFrequency.Slave{slaveAddress}",
                    (ushort)0,
                    cancellationToken,
                ex => PublishFault($"LeiMa.ConnectAsync.Init.ReadBaseFrequency.Slave{slaveAddress}", ex)).ConfigureAwait(false);

                var (ratedCurrentOk, ratedCurrentRaw) = await _safeExecutor.ExecuteAsync(
                    token => ExecuteComSerializedAsync(innerToken => adapter.ReadHoldingRegisterAsync(LeiMaRegisters.RatedCurrent, innerToken), token),
                    $"LeiMa.ConnectAsync.Init.ReadRatedCurrent.Slave{slaveAddress}",
                    (ushort)0,
                    cancellationToken,
                    ex => PublishFault($"LeiMa.ConnectAsync.Init.ReadRatedCurrent.Slave{slaveAddress}", ex)).ConfigureAwait(false);

                DebugLogger.Info("LoopTrack初始化参数下发(对齐ZakYip) TrackName={0} Slave={1} CmdStop={2} P001={3} P310={4} P005ReadOk={5} P005Raw={6} P206ReadOk={7} P206Raw={8}",
                    TrackName,
                    slaveAddress,
                    LeiMaRegisters.CommandDecelerateStop,
                    LeiMaRegisters.RunCommandSourceRs485,
                    _maxTorqueRawUnit,
                    baseFrequencyOk,
                    baseFrequencyRaw,
                    ratedCurrentOk,
                    ratedCurrentRaw);
            }
        }

        /// <summary>
        /// 刷新 P3.10 归一化分母：min(P0.04, P0.07, 配置限频)；若读取失败回退为 50Hz。
        /// </summary>
        private async ValueTask RefreshTorqueNormalizationTopHzAsync(CancellationToken cancellationToken) {
            var minP004Hz = 0m;
            var minP007Hz = 0m;
            var hasP004Value = false;
            var hasP007Value = false;
            foreach (var (slaveAddress, adapter) in _slaveClients) {
                // 步骤1：读取各从站 P0.04 最大输出频率，取所有从站最小值。
                var (p004Ok, p004Raw) = await _safeExecutor.ExecuteAsync(
                   token => ExecuteComSerializedAsync(innerToken => adapter.ReadHoldingRegisterAsync(LeiMaRegisters.MaxOutputFrequency, innerToken), token),
                    $"LeiMa.ConnectAsync.ReadMaxOutputFrequency.Slave{slaveAddress}",
                    (ushort)0,
                    cancellationToken,
                    ex => PublishFault($"LeiMa.ConnectAsync.ReadMaxOutputFrequency.Slave{slaveAddress}", ex)).ConfigureAwait(false);
                if (p004Ok) {
                    var p004Hz = LeiMaSpeedConverter.RawUnitToHz(p004Raw);
                    if (p004Hz > 0m) {
                        minP004Hz = hasP004Value ? Math.Min(minP004Hz, p004Hz) : p004Hz;
                        hasP004Value = true;
                    }
                }

                // 步骤2：读取各从站 P0.07 频率给定，取所有从站最小值，作为实际运行上限约束。
                var (p007Ok, p007Raw) = await _safeExecutor.ExecuteAsync(
                    token => ExecuteComSerializedAsync(innerToken => adapter.ReadHoldingRegisterAsync(LeiMaRegisters.FrequencySetpoint, innerToken), token),
                    $"LeiMa.ConnectAsync.ReadFrequencySetpoint.Slave{slaveAddress}",
                    (ushort)0,
                    cancellationToken,
                    ex => PublishFault($"LeiMa.ConnectAsync.ReadFrequencySetpoint.Slave{slaveAddress}", ex)).ConfigureAwait(false);
                if (!p007Ok) {
                    continue;
                }

                var p007Hz = LeiMaSpeedConverter.RawUnitToHz(p007Raw);
                if (p007Hz <= 0m) {
                    continue;
                }

                minP007Hz = hasP007Value ? Math.Min(minP007Hz, p007Hz) : p007Hz;
                hasP007Value = true;
            }

            // 步骤3：综合 P0.04、P0.07 与配置限频三者最小值，确定归一化分母；任何读取失败则回退 50Hz。
            var denominator = _maxOutputHz;
            if (hasP004Value) {
                denominator = Math.Min(denominator, minP004Hz);
            }
            if (hasP007Value) {
                denominator = Math.Min(denominator, minP007Hz);
            }
            if (denominator <= 0m) {
                denominator = 50m;
            }

            _torqueNormalizationTopHz = denominator;
            DebugLogger.Info("LoopTrack归一化分母刷新 stage=LeiMa.ConnectAsync.RefreshTorqueNormalizationTopHz p004MinHz={0} p007MinHz={1} configLimitHz={2} denominatorHz={3}",
                hasP004Value ? minP004Hz : 0m,
                hasP007Value ? minP007Hz : 0m,
                _maxOutputHz,
                _torqueNormalizationTopHz);
        }

        /// <summary>
        /// 连接成功后的非阻断状态同步：逐个从站尝试回读运行状态与故障码，任一成功即更新运行态。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        private async ValueTask TrySyncRunStatusAfterConnectAsync(CancellationToken cancellationToken) {
            foreach (var (slaveAddress, adapter) in _slaveClients) {
                var (runOk, runRaw) = await _safeExecutor.ExecuteAsync(
                    token => ExecuteComSerializedAsync(innerToken => adapter.ReadHoldingRegisterAsync(LeiMaRegisters.RunStatus, innerToken), token),
                    $"LeiMa.ConnectAsync.SyncStatus.RunStatus.Slave{slaveAddress}",
                    (ushort)3,
                    cancellationToken,
                    ex => PublishFault($"LeiMa.ConnectAsync.SyncStatus.RunStatus.Slave{slaveAddress}", ex)).ConfigureAwait(false);
                if (!runOk) {
                    continue;
                }

                var (alarmOk, alarmRaw) = await _safeExecutor.ExecuteAsync(
                    token => ExecuteComSerializedAsync(innerToken => adapter.ReadHoldingRegisterAsync(LeiMaRegisters.AlarmCode, innerToken), token),
                    $"LeiMa.ConnectAsync.SyncStatus.AlarmCode.Slave{slaveAddress}",
                    (ushort)0,
                    cancellationToken,
                    ex => PublishFault($"LeiMa.ConnectAsync.SyncStatus.AlarmCode.Slave{slaveAddress}", ex)).ConfigureAwait(false);
                if (!alarmOk) {
                    continue;
                }

                UpdateRunStatusFromRaw(alarmRaw, runRaw, $"连接完成状态同步(Slave{slaveAddress})");
                return;
            }

            DebugLogger.Warn("LoopTrack连接后状态同步失败 operation=LeiMa.ConnectAsync.SyncStatus slaves={0} 建议=检查主从站地址映射与RS485接线", string.Join(",", _slaveClients.Select(x => x.SlaveAddress)));
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

            // 步骤2：P3.10 是 P2.06 电流百分比，不与速度单位线性同构；设速阶段统一写入启动电流上限。
            var torqueRaw = normalized > 0m ? _maxTorqueRawUnit : (ushort)0;
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
            if (RunStatus != LoopTrackRunStatus.Running && normalized > 0m) {
                DebugLogger.Warn(
                    "LoopTrack设速提示 operationId={0} runStatus={1} targetMmps={2} targetHz={3} 说明=当前轨道未处于运行态，P3.10 写入会成功但实时速度可能保持 0；请先下发 Start 命令再观察反馈速度",
                    operationId,
                    RunStatus,
                    normalized,
                    targetHz);
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

            DebugLogger.Info("LoopTrack设速成功 operationId={0} requestMmps={1} limitedMmps={2} slaves={3}", operationId, speedMmps, normalized, string.Join(",", _slaveClients.Select(x => x.SlaveAddress)));
            TargetSpeedMmps = normalized;
            ResetPidState();
            _pidStartupOpenLoopUntil = normalized > 0m ? DateTime.Now + PidStartupOpenLoopWindow : DateTime.MinValue;
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
            var (resetOk, resetFailedSlaves) = await WriteRegisterToAllSlavesAsync(
                LeiMaRegisters.Command,
                LeiMaRegisters.CommandAlarmReset,
                "LeiMa.StartAsync.ResetBeforeRun",
                cancellationToken).ConfigureAwait(false);
            if (!resetOk) {
                DebugLogger.Warn("LoopTrack启动前复位失败 operationId={0} failedSlaves={1} 结果=未发送正转运行命令 建议=检查从站地址冲突/串口占用/终端电阻", operationId, string.Join(",", resetFailedSlaves));
                return false;
            }
            DebugLogger.Info("LoopTrack启动前复位成功 operationId={0} slaves={1} next=发送正转运行命令", operationId, string.Join(",", _slaveClients.Select(x => x.SlaveAddress)));
            var (success, failedSlaves) = await WriteRegisterToAllSlavesAsync(
                LeiMaRegisters.Command,
                LeiMaRegisters.CommandForwardRun,
                "LeiMa.StartAsync",
                cancellationToken).ConfigureAwait(false);

            if (success) {
                SetRunStatus(LoopTrackRunStatus.Running, "已下发正转运行命令。");
                _nextIdleStatusPollAt = DateTime.MinValue;
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

            // 步骤2：保持 clear-alarm 轻量路径，仅依赖复位命令写入结果。

            SetRunStatus(LoopTrackRunStatus.Stopped, "故障复位完成。");
            DebugLogger.Info("LoopTrack清报警完成 operationId={0} 说明=当前仅发送复位命令，不自动下发正转运行命令", CreateOperationId());
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
            // 步骤1：先采集多从站速度，避免主从站异常导致后续从站完全不被访问。
            var operationId = CreateOperationId();
            var now = DateTime.Now;
            var shouldPollIdleStatusOnly = RunStatus != LoopTrackRunStatus.Running && TargetSpeedMmps <= 0m;
            if (shouldPollIdleStatusOnly) {
                if (now < _nextIdleStatusPollAt) {
                    return;
                }

                await PollStatusOnlyAsync(cancellationToken).ConfigureAwait(false);
                _nextIdleStatusPollAt = now + IdleStatusPollingInterval;
                return;
            }

            var samples = new List<(byte SlaveId, decimal Mmps)>(_slaveClients.Count);
            var failedSlaves = new List<byte>();

            foreach (var (slaveAddress, adapter) in _slaveClients) {
                var (sampleOk, sampleRaw) = await _safeExecutor.ExecuteAsync(
                    token => ExecuteComSerializedAsync(innerToken => adapter.ReadHoldingRegisterAsync(LeiMaRegisters.EncoderFeedbackSpeed, innerToken), token),
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
            var sampledSlaveIds = string.Join(",", samples.Select(x => x.SlaveId));
            var failedSlaveIds = string.Join(",", failedSlaves);
            DebugLogger.Info("Modbus轮询采样摘要 operationId={0} configuredSlaves={1} sampledSlaves={2} failedSlaves={3}", operationId, string.Join(",", _slaveClients.Select(x => x.SlaveAddress)), sampledSlaveIds, failedSlaveIds);

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
        /// 空闲态只轮询运行状态与故障码，避免停机后持续高频读取速度寄存器。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task PollStatusOnlyAsync(CancellationToken cancellationToken) {
            var statusAdapter = _slaveClients.FirstOrDefault(x => x.Adapter.IsConnected).Adapter;
            if (statusAdapter is null) {
                return;
            }

            var (runOk, runRaw) = await _safeExecutor.ExecuteAsync(
                token => ExecuteComSerializedAsync(innerToken => statusAdapter.ReadHoldingRegisterAsync(LeiMaRegisters.RunStatus, innerToken), token),
                "LeiMa.Poll.Idle.RunStatus",
                (ushort)3,
                cancellationToken,
                ex => PublishFault("LeiMa.Poll.Idle.RunStatus", ex)).ConfigureAwait(false);
            var (alarmOk, alarmRaw) = await _safeExecutor.ExecuteAsync(
                token => ExecuteComSerializedAsync(innerToken => statusAdapter.ReadHoldingRegisterAsync(LeiMaRegisters.AlarmCode, innerToken), token),
                "LeiMa.Poll.Idle.AlarmCode",
                (ushort)0,
                cancellationToken,
                ex => PublishFault("LeiMa.Poll.Idle.AlarmCode", ex)).ConfigureAwait(false);

            if (runOk && alarmOk) {
                UpdateRunStatusFromRaw(alarmRaw, runRaw, "空闲轮询状态同步");
            }
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
                    if (StabilizationElapsed >= _stabilizationWindow) {
                        SetStabilizationStatus(LoopTrackStabilizationStatus.Stabilized, $"{message}：稳速达成。");
                    }
                    else {
                        SetStabilizationStatus(LoopTrackStabilizationStatus.Stabilizing, $"{message}：稳速窗口累计中。");
                    }
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
        /// 执行一次 PID 闭环稳速并写入 P3.10（控制量域归一化）。
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
            if (realTimeSpeedMmps <= 0m && DateTime.Now < _pidStartupOpenLoopUntil) {
                return;
            }
            // 步骤2：执行 PID 计算并更新快照状态，供调参日志使用。
            var targetBaseRaw = Convert.ToDecimal(LeiMaSpeedConverter.HzToRawUnit(LeiMaSpeedConverter.MmpsToHz(TargetSpeedMmps)));
            var input = new PidControllerInput(TargetSpeedMmps, realTimeSpeedMmps, false, targetBaseRaw);
            var output = _pidController.Compute(input, _pidState);
            _pidState = output.NextState;

            var now = DateTime.Now;
            var controlTopHz = Math.Min(_maxOutputHz, _torqueNormalizationTopHz);
            var hzPerRaw = (_maxTorqueRawUnit == 0 || controlTopHz <= 0m)
                ? 0m
                : controlTopHz / _maxTorqueRawUnit;
            var torqueUnclampedRawDecimal = output.UnclampedOutput;
            var torqueCommandRawDecimal = Math.Clamp(torqueUnclampedRawDecimal, 0m, Convert.ToDecimal(_maxTorqueRawUnit));
            var torqueOutputClamped = torqueCommandRawDecimal != torqueUnclampedRawDecimal;
            var torqueRaw = (ushort)Math.Clamp((int)decimal.Round(torqueCommandRawDecimal, MidpointRounding.AwayFromZero), 0, _maxTorqueRawUnit);
            torqueRaw = ApplyLaunchTorqueBoost(torqueRaw, output.ErrorSpeedMmps, realTimeSpeedMmps);
            var appliedHzForLog = torqueRaw * hzPerRaw;
            var appliedMmpsForLog = LeiMaSpeedConverter.HzToMmps(appliedHzForLog);

            PidLastUpdatedAt = now;
            PidLastErrorMmps = output.ErrorSpeedMmps;
            PidLastProportionalHz = output.Proportional * hzPerRaw;
            PidLastIntegralHz = output.Integral * hzPerRaw;
            PidLastDerivativeHz = output.Derivative * hzPerRaw;
            PidLastUnclampedOutput = torqueUnclampedRawDecimal;
            PidLastCommandOutput = torqueCommandRawDecimal;
            PidLastOutputClamped = torqueOutputClamped;
            var shouldWriteByInterval = now - _lastTorqueSetpointWrittenAt >= _torqueSetpointWriteInterval;
            if (!shouldWriteByInterval) {
                return;
            }
            if (_lastPidTorqueSetpointRaw.HasValue && _lastPidTorqueSetpointRaw.Value == torqueRaw) {
                return;
            }

            // 步骤3：按节流策略写入 P3.10，并在限幅场景发布事件。
            var operationId = CreateOperationId();
            var (writeSuccess, failedSlaves) = await WriteRegisterToAllSlavesAsync(
                LeiMaRegisters.TorqueSetpoint,
                torqueRaw,
                "LeiMa.PidClosedLoop.WriteTorqueSetpoint",
                cancellationToken).ConfigureAwait(false);
            LogSpeedWrite(
                "PidClosedLoop",
                operationId,
                TargetSpeedMmps,
                appliedMmpsForLog,
                appliedHzForLog,
                torqueRaw,
                writeSuccess,
                failedSlaves,
                realTimeSpeedMmps);
            if (!writeSuccess) {
                DebugLogger.Warn("LoopTrack闭环写入失败 operationId={0} failedSlaves={1} 建议=检查从站地址冲突/串口占用/终端电阻", CreateOperationId(), string.Join(",", failedSlaves));
                return;
            }

            _lastTorqueSetpointWrittenAt = now;
            _lastPidTorqueSetpointRaw = torqueRaw;

            if (torqueOutputClamped) {
                var unclampedHz = torqueUnclampedRawDecimal * hzPerRaw;
                RaiseEventSafely(
                    FrequencySetpointHardClamped,
                    nameof(FrequencySetpointHardClamped),
                    new LoopTrackFrequencySetpointHardClampedEventArgs {
                        RequestedRawUnit = LeiMaSpeedConverter.HzToRawUnit(unclampedHz),
                        RequestedHz = unclampedHz,
                        ClampMaxHz = _maxOutputHz,

                        ClampedRawUnit = LeiMaSpeedConverter.HzToRawUnit(appliedHzForLog),
                        OccurredAt = now
                    });
            }
        }

        /// <summary>
        /// 在低速且正误差阶段施加起步扭矩地板，避免 PID 首轮输出过小导致拉速失败。
        /// </summary>
        /// <param name="baseTorqueRaw">基础扭矩输出。</param>
        /// <param name="errorSpeedMmps">速度误差（目标-实时）。</param>
        /// <param name="realTimeSpeedMmps">实时速度。</param>
        /// <returns>补偿后的扭矩输出。</returns>
        private ushort ApplyLaunchTorqueBoost(ushort baseTorqueRaw, decimal errorSpeedMmps, decimal realTimeSpeedMmps) {
            if (_maxTorqueRawUnit == 0 || TargetSpeedMmps <= 0m) {
                return baseTorqueRaw;
            }

            if (errorSpeedMmps <= 0m) {
                return baseTorqueRaw;
            }

            var speedRatio = Math.Clamp(realTimeSpeedMmps / TargetSpeedMmps, 0m, 1m);
            if (speedRatio >= TorqueLaunchBoostFadeOutRatio) {
                return baseTorqueRaw;
            }

            if (speedRatio <= TorqueLaunchBoostKeepMaxRatio) {
                return _maxTorqueRawUnit;
            }

            var fadeSpan = TorqueLaunchBoostFadeOutRatio - TorqueLaunchBoostKeepMaxRatio;
            if (fadeSpan <= 0m) {
                return baseTorqueRaw;
            }

            var normalized = (speedRatio - TorqueLaunchBoostKeepMaxRatio) / fadeSpan;
            var floorRatio = 1m - (1m - TorqueLaunchBoostMinFloorRatio) * Math.Clamp(normalized, 0m, 1m);
            var floorRaw = (ushort)Math.Clamp((int)decimal.Round(_maxTorqueRawUnit * floorRatio, MidpointRounding.AwayFromZero), 0, _maxTorqueRawUnit);
            return baseTorqueRaw >= floorRaw ? baseTorqueRaw : floorRaw;
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
            PidLastUnclampedOutput = 0m;
            PidLastCommandOutput = 0m;
            PidLastOutputClamped = false;
            _lastTorqueSetpointWrittenAt = DateTime.MinValue;
            _pidStartupOpenLoopUntil = DateTime.MinValue;
            _lastPidTorqueSetpointRaw = null;
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
        /// 构建从站客户端集合，缺省回退到连接配置从站地址。
        /// </summary>
        /// <param name="defaultClient">默认客户端。</param>
        /// <param name="connectionOptions">连接配置。</param>
        /// <param name="slaveClients">外部注入客户端集合。</param>
        /// <returns>从站客户端集合。</returns>
        private static IReadOnlyList<(byte SlaveAddress, ILeiMaModbusClientAdapter Adapter)> BuildSlaveClients(
            ILeiMaModbusClientAdapter defaultClient,
            LoopTrackConnectionOptions? connectionOptions,
            IReadOnlyList<(byte SlaveAddress, ILeiMaModbusClientAdapter Adapter)>? slaveClients) {
            if (slaveClients is null || slaveClients.Count == 0) {
                var slaveAddress = connectionOptions?.SlaveAddress is > 0 and <= 247
                    ? connectionOptions.SlaveAddress
                    : DefaultSlaveAddress;
                return [(slaveAddress, defaultClient)];
            }

            return slaveClients;
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
                    token => ExecuteComSerializedAsync(innerToken => adapter.WriteSingleRegisterAsync(register, value, innerToken), token),
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

        private async ValueTask<T> ExecuteComSerializedAsync<T>(
            Func<CancellationToken, ValueTask<T>> operation,
            CancellationToken cancellationToken) {
            await _comIoGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            finally {
                _comIoGate.Release();
            }
        }

        private async ValueTask ExecuteComSerializedAsync(
            Func<CancellationToken, ValueTask> operation,
            CancellationToken cancellationToken) {
            await _comIoGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                await operation(cancellationToken).ConfigureAwait(false);
            }
            finally {
                _comIoGate.Release();
            }
        }

        /// <summary>
        /// 生成短格式操作编号。
        /// </summary>
        /// <returns>操作编号。</returns>
        private static string CreateOperationId() {
            var leiMaOperationId = OperationIdFactory.CreateShortOperationId();
            return leiMaOperationId;
        }

        /// <summary>
        /// 记录速度写入落盘日志到 looptrack-speed。
        /// </summary>
        /// <param name="scene">写入场景。</param>
        /// <param name="operationId">操作编号。</param>
        /// <param name="requestedSpeedMmps">请求速度（mm/s）。</param>
        /// <param name="appliedSpeedMmps">实际写入对应速度（mm/s）。</param>
        /// <param name="appliedHz">实际写入对应频率（Hz）。</param>
        /// <param name="torqueRaw">P3.10 原始值。</param>
        /// <param name="success">是否写入成功。</param>
        /// <param name="failedSlaves">失败从站。</param>
        /// <param name="realTimeSpeedMmps">写入时反馈速度（mm/s）。</param>
        private void LogSpeedWrite(
            string scene,
            string operationId,
            decimal requestedSpeedMmps,
            decimal appliedSpeedMmps,
            decimal appliedHz,
            ushort torqueRaw,
            bool success,
            IReadOnlyList<byte> failedSlaves,
            decimal? realTimeSpeedMmps = null) {
            if (success) {
                if (!SpeedLogger.IsInfoEnabled) {
                    return;
                }

                SpeedLogger.Info(
                    "LoopTrack速度写入 scene={0} operationId={1} trackName={2} requestMmps={3} appliedMmps={4} appliedHz={5} torqueRaw={6} feedbackMmps={7} slaves={8}",
                    scene,
                    operationId,
                    TrackName,
                    requestedSpeedMmps,
                    appliedSpeedMmps,
                    appliedHz,
                    torqueRaw,
                    realTimeSpeedMmps ?? RealTimeSpeedMmps,
                    string.Join(",", _slaveClients.Select(x => x.SlaveAddress)));
                return;
            }

            if (!SpeedLogger.IsWarnEnabled) {
                return;
            }

            SpeedLogger.Warn(
                "LoopTrack速度写入失败 scene={0} operationId={1} trackName={2} requestMmps={3} appliedMmps={4} appliedHz={5} torqueRaw={6} feedbackMmps={7} failedSlaves={8}",
                scene,
                operationId,
                TrackName,
                requestedSpeedMmps,
                appliedSpeedMmps,
                appliedHz,
                torqueRaw,
                realTimeSpeedMmps ?? RealTimeSpeedMmps,
                string.Join(",", failedSlaves));
        }
    }
}
