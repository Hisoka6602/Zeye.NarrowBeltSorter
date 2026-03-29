using Microsoft.Extensions.Primitives;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Events.Chutes;
using Zeye.NarrowBeltSorter.Core.Enums.Device;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;

namespace Zeye.NarrowBeltSorter.Host.Services {

    /// <summary>
    /// 格口自处理后台服务，负责配置热更新与退出收敛处理。
    /// </summary>
    public sealed class ChuteSelfHandlingHostedService : BackgroundService {
        private readonly ILogger<ChuteSelfHandlingHostedService> _logger;
        private readonly IChuteManager _chuteManager;
        private readonly IConfiguration _configuration;
        private readonly SemaphoreSlim _reloadSignal = new(0);
        private readonly Dictionary<long, EventHandler<ChuteIoStateChangedEventArgs>> _ioStateHandlers = new();
        private IDisposable? _reloadRegistration;
        private string _lastInfraredSignature = string.Empty;

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
                () => _reloadSignal.Release());

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
            // 步骤1：释放配置变更监听，避免停止阶段重复触发。
            _reloadRegistration?.Dispose();
            _reloadRegistration = null;

            // 步骤2：取消开闭日志事件订阅，避免停止后残留事件处理器。
            UnregisterChuteIoStateLogs();

            // 步骤3：最佳努力关闭全部格口并落盘日志。
            await TryCloseAllChutesAsync(cancellationToken).ConfigureAwait(false);

            // 步骤4：关闭全部格口后断开连接。
            await TryDisconnectAsync(cancellationToken).ConfigureAwait(false);

            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 释放服务资源。
        /// </summary>
        public override void Dispose() {
            _reloadRegistration?.Dispose();
            _reloadSignal.Dispose();
            UnregisterChuteIoStateLogs();
            base.Dispose();
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
                    var action = args.NewState == IoState.High ? "Open" : "Close";
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
            // 步骤1：读取当前配置并做签名比较，未变化则直接跳过。
            var options = _configuration.GetSection("Chutes:ZhiQian").Get<ZhiQianChuteOptions>();
            if (options is null || !options.Enabled) {
                return;
            }

            options.NormalizeLegacySingleDevice();
            if (options.Devices.Count == 0) {
                return;
            }

            var infraredSignature = BuildInfraredSignature(options);
            if (string.Equals(_lastInfraredSignature, infraredSignature, StringComparison.Ordinal)) {
                return;
            }

            // 步骤2：连接就绪后逐格口下发红外参数，并输出成功/失败日志。
            if (_chuteManager.ConnectionStatus != DeviceConnectionStatus.Connected) {
                _logger.LogInformation("格口红外参数热更新跳过，当前连接状态为 {ConnectionStatus}。", _chuteManager.ConnectionStatus);
                return;
            }

            var device = options.Devices[0];
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

            // 步骤3：全部处理完成后刷新签名，避免重复下发。
            _lastInfraredSignature = infraredSignature;
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
            var device = options.Devices[0];
            return string.Join(
                "|",
                device.InfraredChuteOptionsMap
                    .OrderBy(kv => kv.Key)
                    .Select(kv => $"{kv.Key}:{kv.Value.DinChannel},{kv.Value.DefaultDirection},{kv.Value.ControlMode},{kv.Value.DefaultSpeedMmps},{kv.Value.DefaultDurationMs},{kv.Value.DefaultDistanceMm},{kv.Value.AccelerationMmps2},{kv.Value.HoldDurationMs},{kv.Value.TriggerDelayMs},{kv.Value.RollerDiameterMm},{kv.Value.DialCode}"));
        }
    }
}
