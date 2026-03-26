using NLog;
using Zeye.NarrowBeltSorter.Core.Enums.Device;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Events.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Utilities.Chutes;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.ZhiQian {

    /// <summary>
    /// 智嵌 32 路网络继电器格口管理器（IChuteManager 实现）。
    /// 负责将 IChuteManager 业务语义映射到 DO 控制命令，
    /// 管理连接状态、格口快照与事件发布。
    /// </summary>
    public sealed class ZhiQianChuteManager : IChuteManager {
        private static readonly Logger Log = LogManager.GetLogger(nameof(ZhiQianChuteManager));

        private readonly ZhiQianChuteOptions _options;
        private readonly IZhiQianModbusClientAdapter _adapter;
        private readonly SafeExecutor _safeExecutor;
        private readonly IReadOnlyDictionary<long, int> _chuteToDoMap;
        private readonly Dictionary<long, ZhiQianChute> _chutes;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        private readonly HashSet<long> _targetChuteIds = new();
        private readonly HashSet<long> _lockedChuteIds = new();

        private long? _forcedChuteId;
        private DeviceConnectionStatus _connectionStatus = DeviceConnectionStatus.Disconnected;
        private CancellationTokenSource? _pollCts;
        private Task? _pollTask;
        private bool _disposed;

        /// <summary>
        /// 初始化智嵌格口管理器。
        /// </summary>
        /// <param name="options">智嵌驱动配置。</param>
        /// <param name="adapter">Modbus 通信适配器。</param>
        /// <param name="safeExecutor">安全执行器。</param>
        public ZhiQianChuteManager(
            ZhiQianChuteOptions options,
            IZhiQianModbusClientAdapter adapter,
            SafeExecutor safeExecutor) {
            // 步骤1：校验配置合法性，有任何非法项则拒绝构造并记录日志。
            var errors = options.Validate();
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
            _chuteToDoMap = options.ChuteToDoMap.ToDictionary(kv => kv.Key, kv => kv.Value);
            _chutes = _chuteToDoMap.Keys
                .ToDictionary(id => id, id => new ZhiQianChute(id, $"Chute-{id}"));
            foreach (var chute in _chutes.Values) {
                chute.ParcelDropped += (_, args) => ParcelDropped?.Invoke(this, args);
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
            get { lock (_writeLock) { return _forcedChuteId; } }
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
            get { lock (_writeLock) { return _connectionStatus; } }
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
            // 步骤4：写后读校验（若 EnableWriteBackVerify=true）。
            // 步骤5：更新内存快照，发布 ForcedChuteChanged 事件。
            var opId = OperationIdFactory.CreateShortOperationId();
            if (chuteId.HasValue && !_chuteToDoMap.ContainsKey(chuteId.Value)) {
                Log.Error("ZhiQian强排格口不在映射中 opId={0} chuteId={1}", opId, chuteId.Value);
                return false;
            }

            return await _safeExecutor.ExecuteAsync(
                async ct => {
                    await _writeLock.WaitAsync(ct).ConfigureAwait(false);
                    try {
                        var old = _forcedChuteId;
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

                            if (_options.EnableWriteBackVerify) {
                                await VerifyDoStateAsync(doIndex, true, opId, ct).ConfigureAwait(false);
                            }

                            if (_chutes.TryGetValue(chuteId.Value, out var chute)) {
                                chute.EnableForceOpenAsync(true, ct).GetAwaiter().GetResult();
                            }
                        }
                        else if (_forcedChuteId.HasValue) {
                            var doIndex = _chuteToDoMap[_forcedChuteId.Value];
                            await _adapter.WriteSingleDoAsync(doIndex, false, ct).ConfigureAwait(false);
                            if (_chutes.TryGetValue(_forcedChuteId.Value, out var prevChute)) {
                                prevChute.EnableForceOpenAsync(false, ct).GetAwaiter().GetResult();
                            }
                        }

                        _forcedChuteId = chuteId;
                        Log.Info("ZhiQian强排更新 opId={0} old={1} new={2}", opId, old, chuteId);
                        ForcedChuteChanged?.Invoke(this, new ForcedChuteChangedEventArgs {
                            OldForcedChuteId = old,
                            NewForcedChuteId = chuteId,
                            ChangedAt = DateTime.Now
                        });
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
        public ValueTask<bool> AddTargetChuteAsync(long chuteId, CancellationToken cancellationToken = default) {
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
            if (!_chuteToDoMap.TryGetValue(chuteId, out var doIndex)) {
                Log.Error("ZhiQian锁格格口不在映射中 chuteId={0}", chuteId);
                return false;
            }

            var opId = OperationIdFactory.CreateShortOperationId();
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
                        ChuteLockStatusChanged?.Invoke(this, new ChuteLockStatusChangedEventArgs {
                            ChuteId = chuteId,
                            OldIsLocked = oldIsLocked,
                            NewIsLocked = isLocked,
                            ChangedAt = DateTime.Now
                        });
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
            _pollCts = new CancellationTokenSource();
            _pollTask = Task.Run(() => PollLoopAsync(_pollCts.Token));
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
            while (!cancellationToken.IsCancellationRequested) {
                try {
                    await Task.Delay(_options.PollIntervalMs, cancellationToken).ConfigureAwait(false);
                    var states = await _adapter.ReadDoStatesAsync(cancellationToken).ConfigureAwait(false);
                    SyncChuteIoStates(states);
                }
                catch (OperationCanceledException) {
                    break;
                }
                catch (Exception ex) {
                    Log.Warn(ex, "ZhiQian轮询异常，继续轮询");
                    RaiseFaulted("PollLoop", ex);
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
        /// 写后读校验：验证目标 DO 状态是否符合期望。
        /// </summary>
        /// <param name="doIndex">Y 路编号（1~32）。</param>
        /// <param name="expected">期望状态。</param>
        /// <param name="opId">操作编号（用于日志）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private async ValueTask VerifyDoStateAsync(int doIndex, bool expected, string opId, CancellationToken cancellationToken) {
            var states = await _adapter.ReadDoStatesAsync(cancellationToken).ConfigureAwait(false);
            var actual = states[doIndex - ZhiQianAddressMap.DoIndexMin];
            if (actual != expected) {
                Log.Warn("ZhiQian写后读校验不一致 opId={0} Y{1:D2} expected={2} actual={3}", opId, doIndex, expected, actual);
            }
            else {
                Log.Info("ZhiQian写后读校验通过 opId={0} Y{1:D2}={2}", opId, doIndex, actual);
            }
        }

        /// <summary>
        /// 更新连接状态并发布 ConnectionStatusChanged 事件。
        /// </summary>
        /// <param name="newStatus">新状态。</param>
        /// <param name="opId">操作编号（用于日志）。</param>
        private void UpdateConnectionStatus(DeviceConnectionStatus newStatus, string opId) {
            DeviceConnectionStatus old;
            lock (_writeLock) {
                old = _connectionStatus;
                _connectionStatus = newStatus;
            }

            if (old == newStatus) {
                return;
            }

            Log.Info("ZhiQian连接状态变更 opId={0} old={1} new={2}", opId, old, newStatus);
            ConnectionStatusChanged?.Invoke(this, new ChuteManagerConnectionStatusChangedEventArgs {
                OldStatus = old,
                NewStatus = newStatus,
                ChangedAt = DateTime.Now
            });
        }

        /// <summary>
        /// 发布格口配置快照变更事件。
        /// </summary>
        private void RaiseChuteConfigurationChanged() {
            ChuteConfigurationChanged?.Invoke(this, new ChuteConfigurationChangedEventArgs {
                ConfigurationSnapshot = ChuteConfigurationSnapshot,
                ChangedAt = DateTime.Now
            });
        }

        /// <summary>
        /// 发布管理器异常事件。
        /// </summary>
        /// <param name="operation">操作名称。</param>
        /// <param name="ex">异常对象。</param>
        private void RaiseFaulted(string operation, Exception ex) {
            Faulted?.Invoke(this, new ChuteManagerFaultedEventArgs {
                Operation = operation,
                Exception = ex,
                FaultedAt = DateTime.Now
            });
        }
    }
}
