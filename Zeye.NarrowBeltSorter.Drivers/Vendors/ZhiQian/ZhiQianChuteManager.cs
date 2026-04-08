using NLog;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.Chutes;
using Zeye.NarrowBeltSorter.Core.Enums.Device;
using Zeye.NarrowBeltSorter.Core.Events.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;
using Zeye.NarrowBeltSorter.Core.Utilities.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Protocols;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.ZhiQian {

    /// <summary>
    /// 智嵌 32 路网络继电器格口管理器（IChuteManager 实现）。
    /// 负责将 IChuteManager 业务语义映射到 DO 控制命令，
    /// 管理连接状态、格口快照与事件发布。
    /// </summary>
    public sealed class ZhiQianChuteManager : IChuteManager {
        private static readonly Logger Log = LogManager.GetLogger(nameof(ZhiQianChuteManager));

        private readonly ZhiQianChuteOptions _options;
        private readonly IZhiQianClientAdapter _adapter;
        private readonly SafeExecutor _safeExecutor;
        private readonly IInfraredDriverFrameCodec _infraredDriverFrameCodec;
        private readonly IReadOnlyDictionary<long, int> _chuteToDoMap;
        private readonly IReadOnlyDictionary<int, bool> _allDoOffMap;
        private readonly Dictionary<long, ZhiQianChute> _chutes;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly object _stateLock = new();

        private readonly HashSet<long> _targetChuteIds = new();
        private readonly HashSet<long> _lockedChuteIds = new();

        private long? _forcedChuteId;
        private DeviceConnectionStatus _connectionStatus = DeviceConnectionStatus.Disconnected;
        private CancellationTokenSource? _pollCts;
        private Task? _pollTask;
        private bool _disposed;
        private const int PollReconnectFailureThreshold = 3;

        /// <summary>
        /// 初始化智嵌格口管理器。
        /// </summary>
        /// <param name="options">智嵌驱动配置。</param>
        /// <param name="deviceOptions"></param>
        /// <param name="adapter">Modbus 通信适配器。</param>
        /// <param name="safeExecutor">安全执行器。</param>
        public ZhiQianChuteManager(
            ZhiQianChuteOptions options,
            ZhiQianDeviceOptions deviceOptions,
            IZhiQianClientAdapter adapter,
            SafeExecutor safeExecutor,
            IInfraredDriverFrameCodec infraredDriverFrameCodec) {
            // 步骤1：校验配置合法性，有任何非法项则拒绝构造并记录日志。
            var errors = deviceOptions.Validate(0);
            if (errors.Count > 0) {
                foreach (var err in errors) {
                    Log.Error("ZhiQian配置非法 error={0}", err);
                }

                throw new ArgumentException($"ZhiQianChuteOptions 校验失败，共 {errors.Count} 项错误。", nameof(options));
            }

            // 步骤2：初始化格口字典（按 ChuteToDoMap 构造 ZhiQianChute 实例）。
            _options = options;
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _infraredDriverFrameCodec = infraredDriverFrameCodec ?? throw new ArgumentNullException(nameof(infraredDriverFrameCodec));
            _chuteToDoMap = deviceOptions.ChuteToDoMap.ToDictionary(kv => kv.Key, kv => kv.Value);
            _allDoOffMap = _chuteToDoMap.Values.ToDictionary(doIndex => doIndex, _ => false);
            _chutes = _chuteToDoMap.Keys.ToDictionary(id => id, id => {
                var infraredOptions = deviceOptions.InfraredChuteOptionsMap[id];
                return new ZhiQianChute(
                    id,
                    $"Chute-{id}",
                    _chuteToDoMap[id],
                    infraredOptions,
                    _adapter,
                    _infraredDriverFrameCodec,
                    _safeExecutor);
            });
            foreach (var chute in _chutes.Values) {
                chute.ParcelDropped += (_, args) => _safeExecutor.PublishEventAsync(
                    ParcelDropped,
                    this,
                    args,
                    "ZhiQianChuteManager.ParcelDropped");
            }
        }

        /// <inheritdoc />
        public IReadOnlyCollection<IChute> Chutes {
            get {
                lock (_chutes) {
                    return _chutes.Values.ToList();
                }
            }
        }

        /// <inheritdoc />
        public long? ForcedChuteId {
            get { lock (_stateLock) { return _forcedChuteId; } }
        }

        /// <inheritdoc />
        public IReadOnlySet<long> TargetChuteIds {
            get { lock (_targetChuteIds) { return _targetChuteIds.ToHashSet(); } }
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<long, string> ChuteConfigurationSnapshot {
            get {
                lock (_chuteToDoMap) {
                    return _chuteToDoMap.ToDictionary(kv => kv.Key, kv => $"Y{kv.Value:D2}");
                }
            }
        }

        /// <inheritdoc />
        public IReadOnlySet<long> LockedChuteIds {
            get { lock (_lockedChuteIds) { return _lockedChuteIds.ToHashSet(); } }
        }

        /// <inheritdoc />
        public DeviceConnectionStatus ConnectionStatus {
            get { lock (_stateLock) { return _connectionStatus; } }
        }

        /// <inheritdoc />
        public event EventHandler<ChuteParcelDroppedEventArgs>? ParcelDropped;

        /// <inheritdoc />
        public event EventHandler<ForcedChuteChangedEventArgs>? ForcedChuteChanged;

        /// <inheritdoc />
        public event EventHandler<ChuteConfigurationChangedEventArgs>? ChuteConfigurationChanged;

        /// <inheritdoc />
        public event EventHandler<ChuteLockStatusChangedEventArgs>? ChuteLockStatusChanged;

        /// <inheritdoc />
        public event EventHandler<ChuteManagerConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

        /// <inheritdoc />
        public event EventHandler<ChuteManagerFaultedEventArgs>? Faulted;

        /// <inheritdoc />
        public async ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default) {
            // 步骤1：更新状态为 Connecting 并发布事件。
            // 步骤2：建立 Modbus 连接。
            // 步骤3：首次回读全量 DO 状态并同步到格口快照。
            // 步骤4：启动后台轮询任务。
            // 步骤5：更新状态为 Connected 并发布事件。
            var opId = OperationIdFactory.CreateShortOperationId();
            Log.Info("ZhiQian连接开始 opId={0}", opId);
            return await _safeExecutor.ExecuteAsync(
                async ct => {
                    UpdateConnectionStatus(DeviceConnectionStatus.Connecting, opId);
                    await _adapter.ConnectAsync(ct).ConfigureAwait(false);
                    var states = await _adapter.ReadDoStatesAsync(ct).ConfigureAwait(false);
                    SyncChuteIoStates(states);
                    StartPolling();
                    UpdateConnectionStatus(DeviceConnectionStatus.Connected, opId);
                    Log.Info("ZhiQian连接成功 opId={0}", opId);
                },
                $"ZhiQianChuteManager.Connect[{opId}]",
                cancellationToken,
                ex => {
                    Log.Error(ex, "ZhiQian连接失败 opId={0}", opId);
                    UpdateConnectionStatus(DeviceConnectionStatus.Faulted, opId);
                    RaiseFaulted("ConnectAsync", ex);
                }).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async ValueTask<bool> DisconnectAsync(CancellationToken cancellationToken = default) {
            // 步骤1：停止后台轮询任务。
            // 步骤2：断开 Modbus 连接。
            // 步骤3：更新状态为 Disconnected 并发布事件。
            var opId = OperationIdFactory.CreateShortOperationId();
            Log.Info("ZhiQian断开开始 opId={0}", opId);
            return await _safeExecutor.ExecuteAsync(
                async ct => {
                    await StopPollingAsync().ConfigureAwait(false);
                    await _adapter.DisconnectAsync(ct).ConfigureAwait(false);
                    UpdateConnectionStatus(DeviceConnectionStatus.Disconnected, opId);
                    Log.Info("ZhiQian已断开 opId={0}", opId);
                },
                $"ZhiQianChuteManager.Disconnect[{opId}]",
                cancellationToken,
                ex => {
                    Log.Error(ex, "ZhiQian断开失败 opId={0}", opId);
                    RaiseFaulted("DisconnectAsync", ex);
                }).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async ValueTask<bool> SetForcedChuteAsync(long? chuteId, CancellationToken cancellationToken = default) {
            // 步骤1：验证格口 Id 是否在映射中（非空时）。
            // 步骤2：进入写锁，防止并发写冲突。
            // 步骤3：按 ForceOpenExclusive 策略批量关闭其他路，再打开目标路。
            // 步骤4：更新内存快照，发布 ForcedChuteChanged 事件。
            var opId = OperationIdFactory.CreateShortOperationId();
            if (!EnsureConnectedForOperation(nameof(SetForcedChuteAsync), opId)) {
                return false;
            }

            if (chuteId.HasValue && !_chuteToDoMap.ContainsKey(chuteId.Value)) {
                Log.Error("ZhiQian强排格口不在映射中 opId={0} chuteId={1}", opId, chuteId.Value);
                return false;
            }

            if (chuteId.HasValue && IsChuteLocked(chuteId.Value)) {
                Log.Warn("ZhiQian强排冲突，目标格口已锁格 opId={0} chuteId={1}", opId, chuteId.Value);
                return false;
            }

            return await _safeExecutor.ExecuteAsync(
                async ct => {
                    await _writeLock.WaitAsync(ct).ConfigureAwait(false);
                    try {
                        long? old;
                        lock (_stateLock) {
                            old = _forcedChuteId;
                        }

                        if (chuteId.HasValue) {
                            var doIndex = _chuteToDoMap[chuteId.Value];
                            if (_options.ForceOpenExclusive) {
                                var closeMap = _chuteToDoMap
                                    .Where(kv => kv.Key != chuteId.Value)
                                    .ToDictionary(kv => kv.Value, _ => false);
                                closeMap[doIndex] = true;
                                await _adapter.WriteBatchDoAsync(closeMap, ct).ConfigureAwait(false);
                            }
                            else {
                                await _adapter.WriteSingleDoAsync(doIndex, true, ct).ConfigureAwait(false);
                            }

                            // 切换强排时，先清理旧强排的 IsForced 状态（防止多路同时 IsForced）。
                            if (old.HasValue && old != chuteId && _chutes.TryGetValue(old.Value, out var oldChute)) {
                                await oldChute.EnableForceOpenAsync(false, ct).ConfigureAwait(false);
                            }

                            if (_chutes.TryGetValue(chuteId.Value, out var chute)) {
                                await chute.EnableForceOpenAsync(true, ct).ConfigureAwait(false);
                            }
                        }
                        else {
                            await _adapter.WriteBatchDoAsync(_allDoOffMap, ct).ConfigureAwait(false);

                            foreach (var pair in _chutes) {
                                await pair.Value.EnableForceOpenAsync(false, ct).ConfigureAwait(false);
                            }
                        }

                        lock (_stateLock) {
                            _forcedChuteId = chuteId;
                        }

                        Log.Info("ZhiQian强排更新 opId={0} old={1} new={2}", opId, old, chuteId);
                        _safeExecutor.PublishEventAsync(ForcedChuteChanged, this, new ForcedChuteChangedEventArgs {
                            OldForcedChuteId = old,
                            NewForcedChuteId = chuteId,
                            ForcedChuteSet = chuteId.HasValue ? [chuteId.Value] : [],
                            ChangedAt = DateTime.Now
                        }, "ZhiQianChuteManager.ForcedChuteChanged");
                    }
                    finally {
                        _writeLock.Release();
                    }
                },
                $"ZhiQianChuteManager.SetForcedChute[{opId}]",
                cancellationToken,
                ex => {
                    Log.Error(ex, "ZhiQian强排失败 opId={0} chuteId={1}", opId, chuteId);
                    RaiseFaulted("SetForcedChuteAsync", ex);
                }).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async ValueTask<bool> SetForcedChuteSetAsync(IReadOnlyCollection<long> chuteIds, CancellationToken cancellationToken = default) {
            // 步骤1：归一化并校验目标集合（去重、仅保留映射格口、禁止锁格）。
            // 步骤2：进入写锁，统一下发批量 DO（集合内闭合、集合外断开）。
            // 步骤3：同步所有格口 IsForced 快照，并重置单强排快照。
            var opId = OperationIdFactory.CreateShortOperationId();
            if (!EnsureConnectedForOperation(nameof(SetForcedChuteSetAsync), opId)) {
                return false;
            }

            var targetSet = new HashSet<long>();
            foreach (var chuteId in chuteIds) {
                if (!_chuteToDoMap.ContainsKey(chuteId)) {
                    Log.Error("ZhiQian批量强排失败，格口不在映射表中 opId={0} chuteId={1}", opId, chuteId);
                    return false;
                }

                if (IsChuteLocked(chuteId)) {
                    Log.Warn("ZhiQian批量强排冲突，目标格口已锁格 opId={0} chuteId={1}", opId, chuteId);
                    return false;
                }

                targetSet.Add(chuteId);
            }

            return await _safeExecutor.ExecuteAsync(
                async ct => {
                    await _writeLock.WaitAsync(ct).ConfigureAwait(false);
                    try {
                        var writeMap = new Dictionary<int, bool>(_chuteToDoMap.Count);
                        foreach (var mapping in _chuteToDoMap) {
                            writeMap[mapping.Value] = targetSet.Contains(mapping.Key);
                        }

                        await _adapter.WriteBatchDoAsync(writeMap, ct).ConfigureAwait(false);

                        foreach (var chutePair in _chutes) {
                            await chutePair.Value.EnableForceOpenAsync(targetSet.Contains(chutePair.Key), ct).ConfigureAwait(false);
                        }

                        long? old;
                        lock (_stateLock) {
                            old = _forcedChuteId;
                        }

                        lock (_stateLock) {
                            _forcedChuteId = null;
                        }

                        Log.Info("ZhiQian批量强排更新 opId={0} old={1} targetCount={2}", opId, old, targetSet.Count);
                        _safeExecutor.PublishEventAsync(ForcedChuteChanged, this, new ForcedChuteChangedEventArgs {
                            OldForcedChuteId = old,
                            NewForcedChuteId = null,
                            ForcedChuteSet = targetSet.ToArray(),
                            ChangedAt = DateTime.Now
                        }, "ZhiQianChuteManager.ForcedChuteChanged");
                    }
                    finally {
                        _writeLock.Release();
                    }
                },
                $"ZhiQianChuteManager.SetForcedChuteSet[{opId}]",
                cancellationToken,
                ex => {
                    Log.Error(ex, "ZhiQian批量强排失败 opId={0} targetCount={1}", opId, targetSet.Count);
                    RaiseFaulted("SetForcedChuteSetAsync", ex);
                }).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public ValueTask<bool> AddTargetChuteAsync(long chuteId, CancellationToken cancellationToken = default) {
            var opId = OperationIdFactory.CreateShortOperationId();
            if (!EnsureConnectedForOperation(nameof(AddTargetChuteAsync), opId)) {
                return ValueTask.FromResult(false);
            }

            if (!_chuteToDoMap.ContainsKey(chuteId)) {
                return ValueTask.FromResult(false);
            }

            bool changed;
            lock (_targetChuteIds) {
                changed = _targetChuteIds.Add(chuteId);
            }

            if (changed) {
                if (_chutes.TryGetValue(chuteId, out var chute)) {
                    chute.SetIsTarget(true);
                }

                RaiseChuteConfigurationChanged();
            }

            return ValueTask.FromResult(true);
        }

        /// <inheritdoc />
        public ValueTask<bool> RemoveTargetChuteAsync(long chuteId, CancellationToken cancellationToken = default) {
            var opId = OperationIdFactory.CreateShortOperationId();
            if (!EnsureConnectedForOperation(nameof(RemoveTargetChuteAsync), opId)) {
                return ValueTask.FromResult(false);
            }

            bool changed;
            lock (_targetChuteIds) {
                changed = _targetChuteIds.Remove(chuteId);
            }

            if (changed) {
                if (_chutes.TryGetValue(chuteId, out var chute)) {
                    chute.SetIsTarget(false);
                }

                RaiseChuteConfigurationChanged();
            }

            return ValueTask.FromResult(true);
        }

        /// <inheritdoc />
        public async ValueTask<bool> SetChuteLockedAsync(
            long chuteId,
            bool isLocked,
            CancellationToken cancellationToken = default) {
            // 步骤1：验证格口 Id 是否在映射中。
            // 步骤2：锁定时将对应 DO 强制断开（防误开），解锁时恢复可控。
            // 步骤3：更新锁格集合并发布 ChuteLockStatusChanged 事件。
            var opId = OperationIdFactory.CreateShortOperationId();
            if (!EnsureConnectedForOperation(nameof(SetChuteLockedAsync), opId)) {
                return false;
            }

            if (!_chuteToDoMap.TryGetValue(chuteId, out var doIndex)) {
                Log.Error("ZhiQian锁格格口不在映射中 chuteId={0}", chuteId);
                return false;
            }

            return await _safeExecutor.ExecuteAsync(
                async ct => {
                    await _writeLock.WaitAsync(ct).ConfigureAwait(false);
                    try {
                        bool oldIsLocked;
                        lock (_lockedChuteIds) {
                            oldIsLocked = _lockedChuteIds.Contains(chuteId);
                        }

                        if (isLocked) {
                            await _adapter.WriteSingleDoAsync(doIndex, false, ct).ConfigureAwait(false);

                            lock (_lockedChuteIds) {
                                _lockedChuteIds.Add(chuteId);
                            }
                        }
                        else {
                            lock (_lockedChuteIds) {
                                _lockedChuteIds.Remove(chuteId);
                            }
                        }

                        Log.Info("ZhiQian锁格状态更新 opId={0} chuteId={1} isLocked={2}", opId, chuteId, isLocked);
                        _safeExecutor.PublishEventAsync(ChuteLockStatusChanged, this, new ChuteLockStatusChangedEventArgs {
                            ChuteId = chuteId,
                            OldIsLocked = oldIsLocked,
                            NewIsLocked = isLocked,
                            ChangedAt = DateTime.Now
                        }, "ZhiQianChuteManager.ChuteLockStatusChanged");
                    }
                    finally {
                        _writeLock.Release();
                    }
                },
                $"ZhiQianChuteManager.SetChuteLocked[{opId}]",
                cancellationToken,
                ex => {
                    Log.Error(ex, "ZhiQian锁格失败 opId={0} chuteId={1}", opId, chuteId);
                    RaiseFaulted("SetChuteLockedAsync", ex);
                }).ConfigureAwait(false);
        }

        /// <summary>
        /// 按时窗调度指定格口的开闸与关闸动作。
        /// </summary>
        /// <param name="chuteId">格口 Id。</param>
        /// <param name="openAt">开闸本地时间。</param>
        /// <param name="closeAt">关闸本地时间。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>调度是否成功。</returns>
        internal async ValueTask<bool> ScheduleChuteOpenWindowAsync(
            long chuteId,
            DateTime openAt,
            DateTime closeAt,
            CancellationToken ct = default) {
            // 步骤0：校验连接状态、时窗参数与格口映射，再进入安全执行路径。
            var opId = OperationIdFactory.CreateShortOperationId();
            if (!EnsureConnectedForOperation(nameof(ScheduleChuteOpenWindowAsync), opId)) {
                return false;
            }

            if (closeAt <= openAt) {
                Log.Warn("ZhiQian时窗非法，closeAt必须大于openAt opId={0} chuteId={1} openAt={2:O} closeAt={3:O}", opId, chuteId, openAt, closeAt);
                return false;
            }

            if (!_chuteToDoMap.TryGetValue(chuteId, out var doIndex)) {
                Log.Warn("ZhiQian时窗调度失败，格口不在映射中 opId={0} chuteId={1}", opId, chuteId);
                return false;
            }

            if (!_chutes.TryGetValue(chuteId, out var chute)) {
                Log.Warn("ZhiQian时窗调度失败，格口快照不存在 opId={0} chuteId={1}", opId, chuteId);
                return false;
            }

            return await _safeExecutor.ExecuteAsync(
                async cancellationToken => {
                    await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try {
                        // 步骤1：先写入待执行时窗快照。
                        chute.SetPendingIoWindow(openAt, closeAt);

                        // 步骤2：到点开闸并同步状态。
                        var openDelay = openAt - GetLocalNow();
                        if (openDelay > TimeSpan.Zero) {
                            await Task.Delay(openDelay, cancellationToken).ConfigureAwait(false);
                        }

                        await _adapter.WriteSingleDoAsync(doIndex, true, cancellationToken).ConfigureAwait(false);
                        chute.SyncIoState(IoState.High);

                        // 步骤3：到点关闸并提交 Last 时窗快照。
                        var closeDelay = closeAt - GetLocalNow();
                        if (closeDelay > TimeSpan.Zero) {
                            await Task.Delay(closeDelay, cancellationToken).ConfigureAwait(false);
                        }

                        await _adapter.WriteSingleDoAsync(doIndex, false, cancellationToken).ConfigureAwait(false);
                        chute.SyncIoState(IoState.Low);
                        chute.CommitIoWindow();
                    }
                    finally {
                        _writeLock.Release();
                    }
                },
                $"ZhiQianChuteManager.ScheduleChuteOpenWindow[{opId}]",
                ct,
                ex => {
                    Log.Error(ex, "ZhiQian时窗调度失败 opId={0} chuteId={1}", opId, chuteId);
                    RaiseFaulted(nameof(ScheduleChuteOpenWindowAsync), ex);
                }).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public bool TryGetChute(long chuteId, out IChute chute) {
            if (_chutes.TryGetValue(chuteId, out var zhiQianChute)) {
                chute = zhiQianChute;
                return true;
            }

            chute = null!;
            return false;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            await StopPollingAsync().ConfigureAwait(false);
            await _adapter.DisposeAsync().ConfigureAwait(false);
            _writeLock.Dispose();
        }

        /// <summary>
        /// 启动后台 DO 状态轮询任务。
        /// </summary>
        private void StartPolling() {
            // 若已有轮询任务在运行，则避免重复启动以防止并行轮询与 CTS 泄漏。
            if (_pollTask is { IsCompleted: false }) {
                return;
            }

            _pollCts = new CancellationTokenSource();
            _pollTask = PollLoopAsync(_pollCts.Token);
        }

        /// <summary>
        /// 停止后台轮询任务并等待其结束。
        /// </summary>
        private async Task StopPollingAsync() {
            if (_pollCts is null) {
                return;
            }

            try {
                await _pollCts.CancelAsync().ConfigureAwait(false);
                if (_pollTask is not null) {
                    await _pollTask.ConfigureAwait(false);
                }
            }
            catch (Exception ex) {
                Log.Warn(ex, "ZhiQian停止轮询时出现异常");
            }
            finally {
                _pollCts.Dispose();
                _pollCts = null;
                _pollTask = null;
            }
        }

        /// <summary>
        /// 后台轮询主循环：定期回读全量 DO 状态并同步到格口快照。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task PollLoopAsync(CancellationToken cancellationToken) {
            Log.Info("ZhiQian轮询启动 pollIntervalMs={0}", _options.PollIntervalMs);
            var consecutiveReadFailureCount = 0;
            while (!cancellationToken.IsCancellationRequested) {
                try {
                    await Task.Delay(_options.PollIntervalMs, cancellationToken).ConfigureAwait(false);
                    var states = await _adapter.ReadDoStatesAsync(cancellationToken).ConfigureAwait(false);
                    SyncChuteIoStates(states);
                    consecutiveReadFailureCount = 0;
                }
                catch (OperationCanceledException) {
                    break;
                }
                catch (Exception ex) {
                    consecutiveReadFailureCount++;
                    Log.Warn(ex, "ZhiQian轮询异常，继续轮询 failureCount={0}", consecutiveReadFailureCount);
                    RaiseFaulted("PollLoop", ex);
                    if (consecutiveReadFailureCount < PollReconnectFailureThreshold) {
                        continue;
                    }

                    var reconnectSucceeded = await TryReconnectAsync(consecutiveReadFailureCount, cancellationToken).ConfigureAwait(false);
                    consecutiveReadFailureCount = reconnectSucceeded ? 0 : PollReconnectFailureThreshold;
                }
            }

            Log.Info("ZhiQian轮询已停止");
        }

        /// <summary>
        /// 将 32 路 DO 状态同步到格口 IoState 快照。
        /// </summary>
        /// <param name="states">长度为 32 的 DO 状态数组，索引 0 对应 Y01。</param>
        private void SyncChuteIoStates(IReadOnlyList<bool> states) {
            foreach (var (chuteId, doIndex) in _chuteToDoMap) {
                if (!_chutes.TryGetValue(chuteId, out var chute)) {
                    continue;
                }

                var arrayIndex = doIndex - ZhiQianAddressMap.DoIndexMin;
                if (arrayIndex < 0 || arrayIndex >= states.Count) {
                    continue;
                }

                var ioState = states[arrayIndex] ? IoState.High : IoState.Low;
                chute.SyncIoState(ioState);
            }
        }

        /// <summary>
        /// 轮询连续失败后执行自动重连，并在成功后立即全量回读同步快照。
        /// </summary>
        /// <param name="failureCount">连续失败次数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>重连是否成功。</returns>
        private async Task<bool> TryReconnectAsync(int failureCount, CancellationToken cancellationToken) {
            // 步骤1：先置故障态并尝试断开旧连接。
            // 步骤2：进入 Connecting 并执行重新连接。
            // 步骤3：重连后立即全量回读同步，成功则置 Connected。
            var opId = OperationIdFactory.CreateShortOperationId();
            Log.Warn("ZhiQian轮询连续失败触发重连 opId={0} failureCount={1}", opId, failureCount);
            UpdateConnectionStatus(DeviceConnectionStatus.Faulted, opId);
            try {
                await _adapter.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) {
                Log.Warn(ex, "ZhiQian轮询重连前断开失败 opId={0}", opId);
            }

            try {
                UpdateConnectionStatus(DeviceConnectionStatus.Connecting, opId);
                await _adapter.ConnectAsync(cancellationToken).ConfigureAwait(false);
                var states = await _adapter.ReadDoStatesAsync(cancellationToken).ConfigureAwait(false);
                SyncChuteIoStates(states);
                UpdateConnectionStatus(DeviceConnectionStatus.Connected, opId);
                Log.Info("ZhiQian轮询重连成功 opId={0}", opId);
                return true;
            }
            catch (Exception ex) {
                Log.Error(ex, "ZhiQian轮询重连失败 opId={0}", opId);
                UpdateConnectionStatus(DeviceConnectionStatus.Faulted, opId);
                RaiseFaulted("PollLoopReconnect", ex);
                return false;
            }
        }

        /// <summary>
        /// 检查当前连接状态是否允许执行设备相关操作。
        /// </summary>
        /// <param name="operationName">操作名称。</param>
        /// <param name="opId">操作编号。</param>
        /// <returns>是否允许继续执行。</returns>
        private bool EnsureConnectedForOperation(string operationName, string opId) {
            var status = ConnectionStatus;
            if (status == DeviceConnectionStatus.Connected) {
                return true;
            }

            Log.Warn("ZhiQian操作被连接状态门控拦截 operation={0} opId={1} status={2}", operationName, opId, status);
            return false;
        }

        /// <summary>
        /// 判断指定格口是否处于锁格状态。
        /// </summary>
        /// <param name="chuteId">格口 Id。</param>
        /// <returns>是否锁格。</returns>
        private bool IsChuteLocked(long chuteId) {
            lock (_lockedChuteIds) {
                return _lockedChuteIds.Contains(chuteId);
            }
        }

        /// <summary>
        /// 获取当前本地时间（统一本地时间语义，禁止引入时区转换链路；预留后续统一替换时间源的收敛点）。
        /// </summary>
        /// <returns>当前本地时间。</returns>
        private static DateTime GetLocalNow() {
            return DateTime.Now;
        }

        /// <summary>
        /// 更新连接状态并发布 ConnectionStatusChanged 事件。
        /// </summary>
        /// <param name="newStatus">新状态。</param>
        /// <param name="opId">操作编号（用于日志）。</param>
        private void UpdateConnectionStatus(DeviceConnectionStatus newStatus, string opId) {
            DeviceConnectionStatus old;
            lock (_stateLock) {
                old = _connectionStatus;
                _connectionStatus = newStatus;
            }

            if (old == newStatus) {
                return;
            }

            Log.Info("ZhiQian连接状态变更 opId={0} old={1} new={2}", opId, old, newStatus);
            _safeExecutor.PublishEventAsync(ConnectionStatusChanged, this, new ChuteManagerConnectionStatusChangedEventArgs {
                OldStatus = old,
                NewStatus = newStatus,
                ChangedAt = DateTime.Now
            }, "ZhiQianChuteManager.ConnectionStatusChanged");
        }

        /// <summary>
        /// 发布格口配置快照变更事件。
        /// </summary>
        private void RaiseChuteConfigurationChanged() {
            _safeExecutor.PublishEventAsync(ChuteConfigurationChanged, this, new ChuteConfigurationChangedEventArgs {
                ConfigurationSnapshot = ChuteConfigurationSnapshot,
                ChangedAt = DateTime.Now
            }, "ZhiQianChuteManager.ChuteConfigurationChanged");
        }

        /// <summary>
        /// 发布管理器异常事件。
        /// </summary>
        /// <param name="operation">操作名称。</param>
        /// <param name="ex">异常对象。</param>
        private void RaiseFaulted(string operation, Exception ex) {
            _safeExecutor.PublishEventAsync(Faulted, this, new ChuteManagerFaultedEventArgs {
                Operation = operation,
                Exception = ex,
                FaultedAt = DateTime.Now
            }, "ZhiQianChuteManager.Faulted");
        }
    }
}
