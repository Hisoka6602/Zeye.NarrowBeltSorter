using Microsoft.Extensions.Primitives;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Events.Chutes;
using Zeye.NarrowBeltSorter.Core.Enums.Device;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 格口自处理后台服务，负责配置热更新与退出收敛处理。
    /// </summary>
    public sealed class ChuteSelfHandlingHostedService : BackgroundService {
        private const string ActionOpen = "Open";
        private const string ActionClose = "Close";
        private const int ConnectionCheckIntervalMs = 500;

        private readonly ILogger<ChuteSelfHandlingHostedService> _logger;
        private readonly IChuteManager _chuteManager;
        private readonly IConfiguration _configuration;
        private readonly SemaphoreSlim _reloadSignal;
        private readonly Dictionary<long, EventHandler<ChuteIoStateChangedEventArgs>> _ioStateHandlers = new();
        private IDisposable? _reloadRegistration;
        private string _lastInfraredSignature = string.Empty;
        private bool _isWaitingForConnectedLogged;
        private bool _isStopping;

        /// <summary>
        /// 初始化格口自处理后台服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="chuteManager">格口管理器。</param>
        /// <param name="configuration">配置对象。</param>
        public ChuteSelfHandlingHostedService(
            ILogger<ChuteSelfHandlingHostedService> logger,
            IChuteManager chuteManager,
            IConfiguration configuration) {
            _logger = logger;
            _chuteManager = chuteManager;
            _configuration = configuration;
            _reloadSignal = new SemaphoreSlim(0);
        }

        /// <summary>
        /// 执行后台主循环，监听配置变更并实时下发红外参数。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            // 步骤1：注册配置热加载回调，在配置变更时唤醒处理循环。
            _reloadRegistration = ChangeToken.OnChange(
                () => _configuration.GetReloadToken(),
                HandleConfigurationReload);

            // 步骤2：订阅全部格口的开闭事件，确保开闭动作都能落盘日志。
            RegisterChuteIoStateLogs();

            // 步骤3：启动后先尝试一次参数下发，保证首次配置已应用。
            await TryApplyInfraredOptionsAsync("服务启动初始化", stoppingToken).ConfigureAwait(false);

            // 步骤4：循环等待配置变更信号，并在每次变更后实时下发。
            while (!stoppingToken.IsCancellationRequested) {
                try {
                    await _reloadSignal.WaitAsync(stoppingToken).ConfigureAwait(false);
                    await TryApplyInfraredOptionsAsync("配置文件变更", stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    break;
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "格口红外参数热更新处理失败。");
                }
            }
        }

        /// <summary>
        /// 服务停止时执行收敛：关闭全部格口并断开连接。
        /// </summary>
        /// <param name="cancellationToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        public override async Task StopAsync(CancellationToken cancellationToken) {
            // 步骤1：先请求并等待后台循环停止，避免与收敛动作并发。
            _isStopping = true;
            await base.StopAsync(cancellationToken).ConfigureAwait(false);

            // 步骤2：释放配置变更监听，避免停止阶段重复触发。
            _reloadRegistration?.Dispose();
            _reloadRegistration = null;

            // 步骤3：取消开闭日志事件订阅，避免停止后残留事件处理器。
            UnregisterChuteIoStateLogs();

            // 步骤4：最佳努力关闭全部格口并落盘日志。
            await TryCloseAllChutesAsync(cancellationToken).ConfigureAwait(false);

            // 步骤5：关闭全部格口后断开连接。
            await TryDisconnectAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 释放服务资源。
        /// </summary>
        public override void Dispose() {
            try {
                _reloadRegistration?.Dispose();
                _reloadSignal.Dispose();
                UnregisterChuteIoStateLogs();
            }
            finally {
                base.Dispose();
            }
        }

        /// <summary>
        /// 订阅格口 IO 状态变更事件，统一记录开闸/关闸日志。
        /// </summary>
        private void RegisterChuteIoStateLogs() {
            foreach (var chute in _chuteManager.Chutes.OrderBy(x => x.Id)) {
                if (_ioStateHandlers.ContainsKey(chute.Id)) {
                    continue;
                }

                EventHandler<ChuteIoStateChangedEventArgs> handler = (_, args) => {
                    var action = args.NewState == IoState.High ? ActionOpen : ActionClose;
                    _logger.LogInformation(
                        "格口动作落盘日志 chuteId={ChuteId} action={Action} oldState={OldState} newState={NewState} changedAt={ChangedAt:yyyy-MM-dd HH:mm:ss.fff}",
                        args.ChuteId,
                        action,
                        args.OldState,
                        args.NewState,
                        args.ChangedAt);
                };

                chute.IoStateChanged += handler;
                _ioStateHandlers[chute.Id] = handler;
            }
        }

        /// <summary>
        /// 取消格口 IO 状态变更事件订阅。
        /// </summary>
        private void UnregisterChuteIoStateLogs() {
            foreach (var chute in _chuteManager.Chutes.OrderBy(x => x.Id)) {
                if (!_ioStateHandlers.TryGetValue(chute.Id, out var handler)) {
                    continue;
                }

                chute.IoStateChanged -= handler;
            }

            _ioStateHandlers.Clear();
        }

        /// <summary>
        /// 尝试下发红外配置，且仅在配置签名变化时执行写入。
        /// </summary>
        /// <param name="reason">下发原因。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task TryApplyInfraredOptionsAsync(string reason, CancellationToken cancellationToken) {
            // 步骤1：读取配置，仅在功能启用时继续后续流程。
            if (!TryGetEnabledPrimaryDeviceOptions(out _, out _)) {
                return;
            }

            // 步骤2：先等待格口管理器进入 Connected，再执行逐格口参数下发。
            var connected = await WaitForConnectedAsync(cancellationToken).ConfigureAwait(false);
            if (!connected) {
                return;
            }

            // 步骤3：连接就绪后重新读取最新配置，避免等待期间使用过期参数下发。
            if (!TryGetEnabledPrimaryDeviceOptions(out var options, out var device)) {
                return;
            }

            var infraredSignature = BuildInfraredSignature(options);
            if (string.Equals(_lastInfraredSignature, infraredSignature, StringComparison.Ordinal)) {
                return;
            }

            foreach (var (chuteId, infraredOptions) in device.InfraredChuteOptionsMap.OrderBy(kv => kv.Key)) {
                if (!_chuteManager.TryGetChute(chuteId, out var chute)) {
                    _logger.LogWarning("格口红外参数热更新跳过，格口不存在 chuteId={ChuteId}", chuteId);
                    continue;
                }

                var applied = await chute
                    .WriteInfraredChuteOptionsAsync(infraredOptions, reason, cancellationToken)
                    .ConfigureAwait(false);
                if (applied) {
                    _logger.LogInformation("格口红外参数热更新成功 chuteId={ChuteId} reason={Reason}", chuteId, reason);
                }
                else {
                    _logger.LogWarning("格口红外参数热更新失败 chuteId={ChuteId} reason={Reason}", chuteId, reason);
                }
            }

            // 步骤4：全部处理完成后刷新签名，避免重复下发。
            _lastInfraredSignature = infraredSignature;
        }

        /// <summary>
        /// 等待格口管理器进入 Connected 状态。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否已进入 Connected。</returns>
        private async Task<bool> WaitForConnectedAsync(CancellationToken cancellationToken) {
            // 步骤1：若当前已连接，立即返回成功。
            if (_chuteManager.ConnectionStatus == DeviceConnectionStatus.Connected) {
                _isWaitingForConnectedLogged = false;
                return true;
            }

            // 步骤2：先执行一次主动连接尝试，避免在未连接场景长期空等。
            if (await TryConnectAsync(cancellationToken).ConfigureAwait(false)) {
                _isWaitingForConnectedLogged = false;
                return true;
            }

            // 步骤3：循环等待连接状态变更，期间响应取消信号。
            while (!cancellationToken.IsCancellationRequested) {
                try {
                    var status = _chuteManager.ConnectionStatus;
                    if (status == DeviceConnectionStatus.Connected) {
                        _isWaitingForConnectedLogged = false;
                        return true;
                    }

                    if (!_isWaitingForConnectedLogged) {
                        _logger.LogInformation("格口红外参数热更新等待连接完成，当前连接状态为 {ConnectionStatus}。", status);
                        _isWaitingForConnectedLogged = true;
                    }

                    if (status == DeviceConnectionStatus.Disconnected
                        && await TryConnectAsync(cancellationToken).ConfigureAwait(false)) {
                        _isWaitingForConnectedLogged = false;
                        return true;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(ConnectionCheckIntervalMs), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    break;
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "格口红外参数热更新等待连接状态时发生异常。");
                    await Task.Delay(TimeSpan.FromMilliseconds(ConnectionCheckIntervalMs), cancellationToken).ConfigureAwait(false);
                }
            }

            return false;
        }

        /// <summary>
        /// 主动触发一次连接尝试并记录结果。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>连接是否成功。</returns>
        private async Task<bool> TryConnectAsync(CancellationToken cancellationToken) {
            if (_chuteManager.ConnectionStatus == DeviceConnectionStatus.Connected) {
                return true;
            }

            try {
                var connected = await _chuteManager.ConnectAsync(cancellationToken).ConfigureAwait(false);
                if (connected) {
                    _logger.LogInformation("格口连接建立成功。");
                }
                else {
                    _logger.LogWarning("格口连接建立失败。");
                }

                return connected;
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "格口连接建立过程中发生异常。");
                return false;
            }
        }

        /// <summary>
        /// 安全处理配置热加载回调，停止阶段忽略并发释放异常。
        /// </summary>
        private void HandleConfigurationReload() {
            if (_isStopping) {
                return;
            }

            try {
                _reloadSignal.Release();
            }
            catch (ObjectDisposedException ex) {
                _logger.LogDebug(ex, "配置热加载回调在信号量已释放后触发，已忽略。");
            }
            catch (InvalidOperationException ex) {
                _logger.LogDebug(ex, "配置热加载回调在信号量当前状态不允许释放时触发，已忽略。");
            }
        }

        /// <summary>
        /// 读取并校验智嵌配置，提取首台可用设备。
        /// </summary>
        /// <param name="options">智嵌配置。</param>
        /// <param name="device">首台设备配置。</param>
        /// <returns>是否成功获取启用态配置。</returns>
        private bool TryGetEnabledPrimaryDeviceOptions(out ZhiQianChuteOptions options, out ZhiQianDeviceOptions device) {
            options = _configuration.GetSection("Chutes:ZhiQian").Get<ZhiQianChuteOptions>() ?? new ZhiQianChuteOptions();
            if (!options.Enabled) {
                device = default!;
                return false;
            }

            options.NormalizeLegacySingleDevice();
            return TryGetPrimaryDevice(options, out device);
        }

        /// <summary>
        /// 停止阶段关闭全部格口，并记录每个格口关闸日志。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task TryCloseAllChutesAsync(CancellationToken cancellationToken) {
            if (_chuteManager.ConnectionStatus != DeviceConnectionStatus.Connected) {
                _logger.LogInformation("格口关闭跳过，当前连接状态为 {ConnectionStatus}。", _chuteManager.ConnectionStatus);
                return;
            }

            foreach (var chute in _chuteManager.Chutes.OrderBy(x => x.Id)) {
                var closed = await _chuteManager
                    .SetChuteLockedAsync(chute.Id, true, cancellationToken)
                    .ConfigureAwait(false);
                if (closed) {
                    _logger.LogInformation("格口关闭成功 chuteId={ChuteId} action=Close", chute.Id);
                }
                else {
                    _logger.LogWarning("格口关闭失败 chuteId={ChuteId} action=Close", chute.Id);
                }
            }
        }

        /// <summary>
        /// 停止阶段断开格口管理器连接并记录结果。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task TryDisconnectAsync(CancellationToken cancellationToken) {
            if (_chuteManager.ConnectionStatus == DeviceConnectionStatus.Disconnected) {
                _logger.LogInformation("格口连接已断开，无需重复断开。");
                return;
            }

            var disconnected = await _chuteManager.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            if (disconnected) {
                _logger.LogInformation("格口连接断开成功。");
            }
            else {
                _logger.LogWarning("格口连接断开失败。");
            }
        }

        /// <summary>
        /// 构建红外配置签名，用于配置变更去重判定。
        /// </summary>
        /// <param name="options">智嵌配置。</param>
        /// <returns>配置签名。</returns>
        private static string BuildInfraredSignature(ZhiQianChuteOptions options) {
            if (!TryGetPrimaryDevice(options, out var device)) {
                return string.Empty;
            }

            return string.Join(
                "|",
                device.InfraredChuteOptionsMap
                    .OrderBy(kv => kv.Key)
                    .Select(kv => $"{kv.Key}:{SerializeInfraredOptions(kv.Value)}"));
        }

        /// <summary>
        /// 尝试获取当前配置中的首台设备。
        /// </summary>
        /// <param name="options">智嵌配置。</param>
        /// <param name="device">设备配置。</param>
        /// <returns>是否获取成功。</returns>
        private static bool TryGetPrimaryDevice(ZhiQianChuteOptions options, out ZhiQianDeviceOptions device) {
            if (options.Devices.Count > 0) {
                device = options.Devices[0];
                return true;
            }

            device = default!;
            return false;
        }

        /// <summary>
        /// 将红外参数序列化为签名片段。
        /// </summary>
        /// <param name="options">红外参数。</param>
        /// <returns>签名片段。</returns>
        private static string SerializeInfraredOptions(InfraredChuteOptions options) {
            return string.Join(
                ";",
                $"DinChannel={options.DinChannel}",
                $"DefaultDirection={options.DefaultDirection}",
                $"ControlMode={options.ControlMode}",
                $"DefaultSpeedMmps={options.DefaultSpeedMmps}",
                $"DefaultDurationMs={options.DefaultDurationMs}",
                $"DefaultDistanceMm={options.DefaultDistanceMm}",
                $"AccelerationMmps2={options.AccelerationMmps2}",
                $"HoldDurationMs={options.HoldDurationMs}",
                $"TriggerDelayMs={options.TriggerDelayMs}",
                $"RollerDiameterMm={options.RollerDiameterMm}",
                $"DialCode={options.DialCode}");
        }
    }
}
