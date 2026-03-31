using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.System;
using Zeye.NarrowBeltSorter.Core.Events.Carrier;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;
using Zeye.NarrowBeltSorter.Core.Enums.SignalTower;
using Zeye.NarrowBeltSorter.Core.Manager.SignalTower;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 信号塔托管服务：监听系统状态与建环事件，驱动信号塔灯光与蜂鸣器。
    /// 启动预警蜂鸣可被任意新状态取消，状态切换后立即关闭蜂鸣器。
    /// </summary>
    public sealed class SignalTowerHostedService : BackgroundService {
        private readonly ILogger<SignalTowerHostedService> _logger;
        private readonly SafeExecutor _safeExecutor;
        private readonly ISystemStateManager _systemStateManager;
        private readonly ICarrierManager _carrierManager;
        private readonly ISignalTower _signalTower;
        private readonly IOptions<LeadshaineIoPanelStateTransitionOptions> _options;
        private readonly object _buzzerLock = new();

        /// <summary>启动预警蜂鸣取消令牌源，由任意新状态触发取消。</summary>
        private CancellationTokenSource? _startupWarningBuzzerCts;
        private EventHandler<StateChangeEventArgs>? _stateChangedHandler;
        private EventHandler<CarrierRingBuiltEventArgs>? _ringBuiltHandler;

        /// <summary>
        /// 初始化信号塔托管服务。
        /// </summary>
        public SignalTowerHostedService(
            ILogger<SignalTowerHostedService> logger,
            SafeExecutor safeExecutor,
            ISystemStateManager systemStateManager,
            ICarrierManager carrierManager,
            ISignalTower signalTower,
            IOptions<LeadshaineIoPanelStateTransitionOptions> options) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));
            _carrierManager = carrierManager ?? throw new ArgumentNullException(nameof(carrierManager));
            _signalTower = signalTower ?? throw new ArgumentNullException(nameof(signalTower));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// 订阅事件并保活，服务停止时自动退订。
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            SubscribeEvents();
            try {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                // 正常停止，忽略取消异常。
            }
            finally {
                UnsubscribeEvents();
            }
        }

        /// <summary>
        /// 停止时取消活跃蜂鸣并退订事件。
        /// </summary>
        public override Task StopAsync(CancellationToken cancellationToken) {
            CancelStartupWarningBuzzer();
            return base.StopAsync(cancellationToken);
        }

        /// <summary>
        /// 订阅系统状态变更与小车建环事件。
        /// </summary>
        private void SubscribeEvents() {
            _stateChangedHandler = (_, args) => OnStateChanged(args);
            _ringBuiltHandler = (_, _) => OnRingBuilt();
            _systemStateManager.StateChanged += _stateChangedHandler;
            _carrierManager.RingBuilt += _ringBuiltHandler;
        }

        /// <summary>
        /// 系统状态变更处理：任意新状态到来时先取消启动预警蜂鸣，再执行对应灯光与蜂鸣逻辑。
        /// </summary>
        private void OnStateChanged(StateChangeEventArgs args) {
            // 步骤1：任意新状态到来时立即取消启动预警蜂鸣等待。
            CancelStartupWarningBuzzer();

            // 步骤2：根据新状态驱动对应信号塔输出。
            switch (args.NewState) {
                case SystemState.Paused:
                case SystemState.Booting:
                case SystemState.Ready:
                    _ = _safeExecutor.ExecuteAsync(
                        () => _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Off).AsTask(),
                        "SignalTower.SetLight.Off");
                    break;

                case SystemState.EmergencyStop:
                    _ = _safeExecutor.ExecuteAsync(async () => {
                        await _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Red).ConfigureAwait(false);
                        await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.On).ConfigureAwait(false);
                        await Task.Delay(2000).ConfigureAwait(false);
                        await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.Off).ConfigureAwait(false);
                    }, "SignalTower.EmergencyStop");
                    break;

                case SystemState.StartupWarning:
                    _ = _safeExecutor.ExecuteAsync(
                        () => _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Yellow).AsTask(),
                        "SignalTower.SetLight.Yellow");
                    StartStartupWarningBuzzer();
                    break;
            }
        }

        /// <summary>
        /// 小车建环完成处理：切换为绿灯。
        /// </summary>
        private void OnRingBuilt() {
            _ = _safeExecutor.ExecuteAsync(
                () => _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Green).AsTask(),
                "SignalTower.SetLight.Green");
        }

        /// <summary>
        /// 启动启动预警蜂鸣任务：蜂鸣开启后等待配置时长，期间可被取消。
        /// 取消后立即关闭蜂鸣器，正常结束时同样关闭蜂鸣器。
        /// </summary>
        private void StartStartupWarningBuzzer() {
            CancellationToken token;
            lock (_buzzerLock) {
                var cts = new CancellationTokenSource();
                _startupWarningBuzzerCts = cts;
                token = cts.Token;
            }

            _ = _safeExecutor.ExecuteAsync(async () => {
                await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.On).ConfigureAwait(false);
                try {
                    // 等待配置时长，可被状态切换打断。
                    await Task.Delay(_options.Value.StartupWarningDurationMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    _logger.LogInformation("启动预警蜂鸣已被新状态取消，立即关闭蜂鸣器。");
                }
                // 无论正常结束还是取消，均关闭蜂鸣器。
                await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.Off).ConfigureAwait(false);
            }, "SignalTower.StartupWarningBuzzer");
        }

        /// <summary>
        /// 取消当前活跃的启动预警蜂鸣任务。
        /// </summary>
        private void CancelStartupWarningBuzzer() {
            CancellationTokenSource? cts;
            lock (_buzzerLock) {
                cts = _startupWarningBuzzerCts;
                _startupWarningBuzzerCts = null;
            }
            if (cts is null) {
                return;
            }
            cts.Cancel();
            cts.Dispose();
        }

        /// <summary>
        /// 退订所有已订阅事件。
        /// </summary>
        private void UnsubscribeEvents() {
            if (_stateChangedHandler is not null) {
                _systemStateManager.StateChanged -= _stateChangedHandler;
                _stateChangedHandler = null;
            }
            if (_ringBuiltHandler is not null) {
                _carrierManager.RingBuilt -= _ringBuiltHandler;
                _ringBuiltHandler = null;
            }
        }
    }
}
