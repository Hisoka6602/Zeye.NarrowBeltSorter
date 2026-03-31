using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Enums.Device;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.System;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 格口强排后台服务。
    /// 支持两种互斥模式，轮转模式优先：
    /// <list type="bullet">
    ///   <item>轮转模式：依赖 <see cref="ChuteForcedRotationOptions.ChuteSequence"/> 与切换间隔，按顺序循环切换强排口。</item>
    ///   <item>固定模式：依赖 <see cref="ChuteForcedRotationOptions.FixedChuteId"/>，系统 Running 时闭合，非 Running 时自动断开。</item>
    /// </list>
    /// </summary>
    public sealed class ChuteForcedRotationHostedService : BackgroundService {
        private readonly ILogger<ChuteForcedRotationHostedService> _logger;
        private readonly IChuteManager _chuteManager;
        private readonly ISystemStateManager _systemStateManager;
        private readonly IOptionsMonitor<ChuteForcedRotationOptions> _optionsMonitor;
        private readonly object _stateSync = new();
        private readonly SemaphoreSlim _stateSignal = new(0, 1);
        private IDisposable? _optionsChangedRegistration;
        private EventHandler<StateChangeEventArgs>? _stateChangedHandler;
        private SystemState _pendingState;
        private bool _hasPendingState;
        private bool _needsApplyAfterReconnect;
        private bool _needsApplyAfterOptionsChanged;
        private bool _rotationEmptyConfigurationWarningLogged;
        private bool _rotationInvalidIntervalWarningLogged;

        /// <summary>
        /// 初始化格口强排后台服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="chuteManager">格口管理器。</param>
        /// <param name="systemStateManager">系统状态管理器。</param>
        /// <param name="optionsMonitor">强排配置监听器。</param>
        public ChuteForcedRotationHostedService(
            ILogger<ChuteForcedRotationHostedService> logger,
            IChuteManager chuteManager,
            ISystemStateManager systemStateManager,
            IOptionsMonitor<ChuteForcedRotationOptions> optionsMonitor) {
            _logger = logger;
            _chuteManager = chuteManager;
            _systemStateManager = systemStateManager;
            _optionsMonitor = optionsMonitor;
        }

        /// <summary>
        /// 执行后台主循环：根据配置选择轮转或固定模式。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            var options = _optionsMonitor.CurrentValue;
            // 步骤1：检查服务总开关。
            if (!options.Enabled) {
                _logger.LogInformation("格口强排后台服务已禁用。");
                return;
            }
            _optionsChangedRegistration = _optionsMonitor.OnChange(OnForcedRotationOptionsChanged);
            _logger.LogInformation(
                "格口强排后台服务配置快照 enabled={Enabled} fixedChuteId={FixedChuteId} sequenceCount={SequenceCount} switchIntervalSeconds={SwitchIntervalSeconds}",
                options.Enabled,
                options.FixedChuteId,
                options.ChuteSequence.Count,
                options.SwitchIntervalSeconds);
            // 步骤2：轮转模式优先；ChuteSequence 非空时忽略 FixedChuteId。
            if (options.ChuteSequence.Count > 0) {
                _logger.LogInformation("格口强排后台服务进入轮转模式（ChuteSequence 非空，FixedChuteId 将被忽略）。");
                await ExecuteRotationModeAsync(stoppingToken).ConfigureAwait(false);
                return;
            }

            if (options.FixedChuteId.HasValue) {
                _logger.LogInformation("格口强排后台服务进入固定模式（仅 Running 状态会闭合强排）。");
                await ExecuteFixedModeAsync(stoppingToken).ConfigureAwait(false);
                return;
            }

            _logger.LogWarning("格口强排后台服务未配置有效的轮转序列或固定强排口，服务退出。");
        }

        /// <summary>
        /// 停止服务并保证信号量被释放，防止主循环永久阻塞。
        /// </summary>
        /// <param name="cancellationToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        public override async Task StopAsync(CancellationToken cancellationToken) {
            TryUnsubscribeStateChanged();
            TryUnregisterOptionsChanged();

            lock (_stateSync) {
                TryReleaseStateSignal();
            }

            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 释放服务资源，包括信号量。
        /// </summary>
        public override void Dispose() {
            _stateSignal.Dispose();
            TryUnregisterOptionsChanged();
            base.Dispose();
        }

        /// <summary>
        /// 轮转模式主循环：等待格口管理器连接后，按数组顺序循环切换强排口。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        private async Task ExecuteRotationModeAsync(CancellationToken stoppingToken) {
            var index = 0;

            // 步骤0：尝试建立连接；失败时仅记录日志，主循环会持续等待 Connected 状态再执行强排。
            var connected = await _chuteManager.ConnectAsync(stoppingToken).ConfigureAwait(false);
            if (!connected) {
                _logger.LogWarning("格口强排轮转初始连接失败，将在主循环中持续等待连接就绪。");
            }

            while (!stoppingToken.IsCancellationRequested) {
                // 步骤1：等待格口管理器进入 Connected 状态，再触发强排切换。
                if (_chuteManager.ConnectionStatus != DeviceConnectionStatus.Connected) {
                    _logger.LogInformation("格口强排轮转等待连接完成 currentStatus={ConnectionStatus}", _chuteManager.ConnectionStatus);
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var options = _optionsMonitor.CurrentValue;
                var sequence = options.ChuteSequence.ToArray();
                if (sequence.Length == 0) {
                    if (!_rotationEmptyConfigurationWarningLogged) {
                        _logger.LogWarning("格口强排轮转配置为空，等待下一次配置变更。");
                        _rotationEmptyConfigurationWarningLogged = true;
                    }
                    _rotationInvalidIntervalWarningLogged = false;
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (options.SwitchIntervalSeconds < 1) {
                    if (!_rotationInvalidIntervalWarningLogged) {
                        _logger.LogWarning("格口强排轮转后台服务配置非法，SwitchIntervalSeconds 必须大于等于 1。");
                        _rotationInvalidIntervalWarningLogged = true;
                    }
                    _rotationEmptyConfigurationWarningLogged = false;
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                _rotationEmptyConfigurationWarningLogged = false;
                _rotationInvalidIntervalWarningLogged = false;
                index %= sequence.Length;

                // 步骤2：按数组索引执行强排并推进索引，形成循环轮转。
                var chuteId = sequence[index];
                var switched = await _chuteManager.SetForcedChuteAsync(chuteId, stoppingToken).ConfigureAwait(false);
                if (switched) {
                    _logger.LogInformation("格口强排轮转成功 chuteId={ChuteId} index={Index}", chuteId, index);
                }
                else {
                    _logger.LogWarning("格口强排轮转失败 chuteId={ChuteId} index={Index}", chuteId, index);
                }

                index = (index + 1) % sequence.Length;
                await Task.Delay(TimeSpan.FromSeconds(options.SwitchIntervalSeconds), stoppingToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 固定模式主循环：订阅系统状态变更事件，Running 时闭合固定格口，非 Running 时断开。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        private async Task ExecuteFixedModeAsync(CancellationToken stoppingToken) {
            var initialFixedChuteId = _optionsMonitor.CurrentValue.FixedChuteId;
            if (!initialFixedChuteId.HasValue || initialFixedChuteId.Value <= 0) {
                _logger.LogWarning("格口固定强排配置非法：FixedChuteId 必须为正数。当前值={FixedChuteId}", initialFixedChuteId);
                return;
            }

            var fixedChuteId = initialFixedChuteId.Value;
            _logger.LogInformation("格口固定强排后台服务启动 fixedChuteId={FixedChuteId}", fixedChuteId);
            // 步骤0：尝试建立连接；失败时仅记录日志，状态循环中将按 ConnectionStatus 跳过未连接帧。
            var connected = await _chuteManager.ConnectAsync(stoppingToken).ConfigureAwait(false);
            if (!connected) {
                _logger.LogWarning("格口固定强排初始连接失败，将在状态变更时按连接状态决策是否应用强排。");
                _needsApplyAfterReconnect = true;
            }

            // 步骤1：订阅系统状态变更事件，确保后续状态切换均能驱动格口动作。
            _stateChangedHandler = OnSystemStateChanged;
            _systemStateManager.StateChanged += _stateChangedHandler;

            try {
                // 步骤2：注入初始状态为待处理状态，确保服务启动后无论连接先后均会应用一次固定强排策略。
                lock (_stateSync) {
                    _pendingState = _systemStateManager.CurrentState;
                    _hasPendingState = true;
                    TryReleaseStateSignal();
                }

                while (!stoppingToken.IsCancellationRequested) {
                    try {
                        await _stateSignal.WaitAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) {
                        break;
                    }
                    if (_chuteManager.ConnectionStatus != DeviceConnectionStatus.Connected) {
                        bool reconnect;
                        try {
                            reconnect = await _chuteManager.ConnectAsync(stoppingToken).ConfigureAwait(false);
                        }
                        catch (Exception ex) {
                            _logger.LogError(ex, "格口固定强排重连格口管理器时发生异常，将在后续状态触发时重试。");
                            lock (_stateSync) {
                                _needsApplyAfterReconnect = true;
                            }
                            continue;
                        }
                        if (!reconnect) {
                            _logger.LogWarning("格口固定强排重连失败，等待下一次重试。");
                            lock (_stateSync) {
                                _needsApplyAfterReconnect = true;
                            }
                            continue;
                        }

                        _logger.LogInformation("格口固定强排重连成功，将按当前系统状态补偿应用。");
                        lock (_stateSync) {
                            _needsApplyAfterReconnect = true;
                        }
                    }
                    SystemState newState;
                    lock (_stateSync) {
                        if (!_hasPendingState && !_needsApplyAfterReconnect && !_needsApplyAfterOptionsChanged) {
                            continue;
                        }

                        newState = _hasPendingState
                            ? _pendingState
                            : _systemStateManager.CurrentState;
                        _hasPendingState = false;
                        _needsApplyAfterReconnect = false;
                        _needsApplyAfterOptionsChanged = false;
                    }

                    // 步骤3：兜底防护，避免在读状态后连接再次断开时丢失待应用状态。
                    if (_chuteManager.ConnectionStatus != DeviceConnectionStatus.Connected) {
                        var reconnectResult = false;
                        try {
                            reconnectResult = await _chuteManager.ConnectAsync(stoppingToken).ConfigureAwait(false);
                        }
                        catch (Exception ex) {
                            _logger.LogError(ex, "格口固定强排重连格口管理器时发生异常，将在后续状态触发时重试。");
                        }
                        lock (_stateSync) {
                            _pendingState = newState;
                            _hasPendingState = true;
                            TryReleaseStateSignal();
                        }
                        _logger.LogWarning(
                            "格口固定强排跳过状态应用，格口管理器未连接 state={State} reconnectResult={ReconnectResult}",
                            newState,
                            reconnectResult);
                        continue;
                    }

                    try {
                        await ApplyFixedForcedChuteAsync(newState, stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) {
                        _logger.LogError(ex, "格口固定强排应用状态时发生异常。");
                    }
                }
            }
            finally {
                TryUnsubscribeStateChanged();

                // 步骤4：服务停止时强制断开强排，保证设备侧不残留强排状态；使用 5 秒超时防止无限阻塞。
                if (_chuteManager.ConnectionStatus == DeviceConnectionStatus.Connected) {
                    using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var cleanupToken = cleanupCts.Token;
                    try {
                        var success = await _chuteManager.SetForcedChuteAsync(null, cleanupToken).ConfigureAwait(false);
                        if (success) {
                            _logger.LogInformation(
                                "停止清理阶段已断开固定强排 fixedChuteId={FixedChuteId}",
                                fixedChuteId);
                        }
                        else {
                            _logger.LogWarning(
                                "停止清理阶段断开固定强排返回失败 fixedChuteId={FixedChuteId} connectionStatus={ConnectionStatus}",
                                fixedChuteId,
                                _chuteManager.ConnectionStatus);
                        }
                    }
                    catch (OperationCanceledException) when (cleanupToken.IsCancellationRequested) {
                        _logger.LogWarning(
                            "停止清理阶段断开固定强排超时 fixedChuteId={FixedChuteId} connectionStatus={ConnectionStatus}",
                            fixedChuteId,
                            _chuteManager.ConnectionStatus);
                    }
                    catch (Exception ex) {
                        _logger.LogError(
                            ex,
                            "停止清理阶段断开固定强排异常 fixedChuteId={FixedChuteId} connectionStatus={ConnectionStatus}",
                            fixedChuteId,
                            _chuteManager.ConnectionStatus);
                    }
                }
            }
        }

        /// <summary>
        /// 根据系统状态闭合或断开固定强排格口。
        /// Running → 闭合；其他状态 → 断开。
        /// </summary>
        /// <param name="state">当前系统状态。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task ApplyFixedForcedChuteAsync(SystemState state, CancellationToken cancellationToken) {
            // 步骤1：读取最新固定强排配置，非法时在 Running 状态主动断开已闭合强排。
            var fixedChuteId = _optionsMonitor.CurrentValue.FixedChuteId;
            if (!fixedChuteId.HasValue || fixedChuteId.Value <= 0) {
                if (state == SystemState.Running) {
                    var resetResult = await _chuteManager.SetForcedChuteAsync(null, cancellationToken).ConfigureAwait(false);
                    if (resetResult) {
                        _logger.LogInformation("格口固定强排已断开，原因=FixedChuteId 非法。");
                    }
                    else {
                        _logger.LogWarning("格口固定强排断开失败，原因=FixedChuteId 非法。");
                    }
                }
                _logger.LogWarning("格口固定强排应用跳过：当前 FixedChuteId 非法。");
                return;
            }

            // 步骤2：按系统状态应用强排，Running 闭合、非 Running 断开。
            if (state == SystemState.Running) {
                var result = await _chuteManager.SetForcedChuteAsync(fixedChuteId.Value, cancellationToken).ConfigureAwait(false);
                if (result) {
                    _logger.LogInformation("格口固定强排已闭合 chuteId={ChuteId}", fixedChuteId.Value);
                }
                else {
                    _logger.LogWarning("格口固定强排闭合失败 chuteId={ChuteId}", fixedChuteId.Value);
                }
            }
            else {
                var result = await _chuteManager.SetForcedChuteAsync(null, cancellationToken).ConfigureAwait(false);
                if (result) {
                    _logger.LogInformation("格口固定强排已断开 state={State}", state);
                }
                else {
                    _logger.LogWarning("格口固定强排断开失败 state={State}", state);
                }
            }
        }

        /// <summary>
        /// 系统状态变更事件处理：仅保留最新状态并通知主循环。
        /// </summary>
        /// <param name="sender">事件发送方。</param>
        /// <param name="args">状态变更参数。</param>
        private void OnSystemStateChanged(object? sender, StateChangeEventArgs args) {
            try {
                lock (_stateSync) {
                    var shouldSignal = !_hasPendingState;
                    _pendingState = args.NewState;
                    _hasPendingState = true;
                    if (shouldSignal) {
                        TryReleaseStateSignal();
                    }
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "格口固定强排投递系统状态失败。");
            }
        }

        /// <summary>
        /// 监听强排配置变化并主动触发一次状态应用，确保固定模式参数修改可实时生效。
        /// </summary>
        /// <param name="_">最新配置快照。</param>
        private void OnForcedRotationOptionsChanged(ChuteForcedRotationOptions _) {
            try {
                lock (_stateSync) {
                    _pendingState = _systemStateManager.CurrentState;
                    _hasPendingState = true;
                    _needsApplyAfterOptionsChanged = true;
                    TryReleaseStateSignal();
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "格口固定强排处理配置变更通知失败。");
            }
        }

        /// <summary>
        /// 释放配置变更监听。
        /// </summary>
        private void TryUnregisterOptionsChanged() {
            try {
                _optionsChangedRegistration?.Dispose();
                _optionsChangedRegistration = null;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "格口固定强排卸载配置变更监听失败。");
            }
        }

        /// <summary>
        /// 卸载系统状态变更订阅，防止内存泄漏。
        /// </summary>
        private void TryUnsubscribeStateChanged() {
            try {
                if (_stateChangedHandler != null) {
                    _systemStateManager.StateChanged -= _stateChangedHandler;
                    _stateChangedHandler = null;
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "格口固定强排卸载系统状态订阅失败。");
            }
        }

        /// <summary>
        /// 安全释放状态信号量，避免重复释放引发异常。
        /// </summary>
        private void TryReleaseStateSignal() {
            try {
                if (_stateSignal.CurrentCount == 0) {
                    _stateSignal.Release();
                }
            }
            catch (SemaphoreFullException ex) {
                _logger.LogDebug(ex, "格口固定强排状态信号量已满，忽略重复释放。");
            }
        }
    }
}
