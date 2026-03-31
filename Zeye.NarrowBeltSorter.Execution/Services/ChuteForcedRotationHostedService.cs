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
        private readonly ChuteForcedRotationOptions _options;
        private readonly object _stateSync = new();
        private readonly SemaphoreSlim _stateSignal = new(0, 1);
        private EventHandler<StateChangeEventArgs>? _stateChangedHandler;
        private SystemState _pendingState;
        private bool _hasPendingState;

        /// <summary>
        /// 初始化格口强排后台服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="chuteManager">格口管理器。</param>
        /// <param name="systemStateManager">系统状态管理器。</param>
        /// <param name="options">强排配置。</param>
        public ChuteForcedRotationHostedService(
            ILogger<ChuteForcedRotationHostedService> logger,
            IChuteManager chuteManager,
            ISystemStateManager systemStateManager,
            IOptions<ChuteForcedRotationOptions> options) {
            _logger = logger;
            _chuteManager = chuteManager;
            _systemStateManager = systemStateManager;
            _options = options.Value;
        }

        /// <summary>
        /// 执行后台主循环：根据配置选择轮转或固定模式。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            // 步骤1：检查服务总开关。
            if (!_options.Enabled) {
                _logger.LogInformation("格口强排后台服务已禁用。");
                return;
            }

            // 步骤2：轮转模式优先；ChuteSequence 非空时忽略 FixedChuteId。
            if (_options.ChuteSequence.Count > 0) {
                await ExecuteRotationModeAsync(stoppingToken).ConfigureAwait(false);
                return;
            }

            if (_options.FixedChuteId.HasValue) {
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
            base.Dispose();
        }

        /// <summary>
        /// 轮转模式主循环：等待格口管理器连接后，按数组顺序循环切换强排口。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        private async Task ExecuteRotationModeAsync(CancellationToken stoppingToken) {
            if (_options.SwitchIntervalSeconds < 1) {
                _logger.LogWarning("格口强排轮转后台服务配置非法，SwitchIntervalSeconds 必须大于等于 1。");
                return;
            }

            var sequence = _options.ChuteSequence.ToArray();
            var index = 0;
            _logger.LogInformation(
                "格口强排轮转后台服务启动 sequence={Sequence} switchIntervalSeconds={SwitchIntervalSeconds}",
                string.Join(",", sequence),
                _options.SwitchIntervalSeconds);

            await _chuteManager.ConnectAsync(stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested) {
                // 步骤1：等待格口管理器进入 Connected 状态，再触发强排切换。
                if (_chuteManager.ConnectionStatus != DeviceConnectionStatus.Connected) {
                    _logger.LogInformation("格口强排轮转等待连接完成 currentStatus={ConnectionStatus}", _chuteManager.ConnectionStatus);
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                    continue;
                }

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
                await Task.Delay(TimeSpan.FromSeconds(_options.SwitchIntervalSeconds), stoppingToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 固定模式主循环：订阅系统状态变更事件，Running 时闭合固定格口，非 Running 时断开。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        private async Task ExecuteFixedModeAsync(CancellationToken stoppingToken) {
            var fixedChuteId = _options.FixedChuteId!.Value;
            _logger.LogInformation("格口固定强排后台服务启动 fixedChuteId={FixedChuteId}", fixedChuteId);

            await _chuteManager.ConnectAsync(stoppingToken).ConfigureAwait(false);

            // 步骤1：订阅系统状态变更事件，确保后续状态切换均能驱动格口动作。
            _stateChangedHandler = OnSystemStateChanged;
            _systemStateManager.StateChanged += _stateChangedHandler;

            try {
                // 步骤2：处理初始状态，防止服务启动时状态已为 Running 但未触发事件。
                await ApplyFixedForcedChuteAsync(fixedChuteId, _systemStateManager.CurrentState, stoppingToken).ConfigureAwait(false);

                while (!stoppingToken.IsCancellationRequested) {
                    try {
                        await _stateSignal.WaitAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) {
                        break;
                    }

                    SystemState newState;
                    lock (_stateSync) {
                        if (!_hasPendingState) {
                            continue;
                        }

                        newState = _pendingState;
                        _hasPendingState = false;
                    }

                    // 步骤3：仅在格口管理器已连接时应用强排动作；未连接时跳过本次状态。
                    if (_chuteManager.ConnectionStatus != DeviceConnectionStatus.Connected) {
                        _logger.LogWarning("格口固定强排跳过状态应用，格口管理器未连接 state={State}", newState);
                        continue;
                    }

                    try {
                        await ApplyFixedForcedChuteAsync(fixedChuteId, newState, stoppingToken).ConfigureAwait(false);
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
                    await _chuteManager.SetForcedChuteAsync(null, cleanupCts.Token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// 根据系统状态闭合或断开固定强排格口。
        /// Running → 闭合；其他状态 → 断开。
        /// </summary>
        /// <param name="fixedChuteId">固定强排格口 Id。</param>
        /// <param name="state">当前系统状态。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task ApplyFixedForcedChuteAsync(long fixedChuteId, SystemState state, CancellationToken cancellationToken) {
            if (state == SystemState.Running) {
                var result = await _chuteManager.SetForcedChuteAsync(fixedChuteId, cancellationToken).ConfigureAwait(false);
                if (result) {
                    _logger.LogInformation("格口固定强排已闭合 chuteId={ChuteId}", fixedChuteId);
                }
                else {
                    _logger.LogWarning("格口固定强排闭合失败 chuteId={ChuteId}", fixedChuteId);
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