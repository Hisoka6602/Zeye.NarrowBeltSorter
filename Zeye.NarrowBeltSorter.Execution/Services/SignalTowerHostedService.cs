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
    /// 所有蜂鸣任务共用互斥取消机制与代际号，状态切换时旧蜂鸣立即被取消，新状态接管蜂鸣控制。
    /// </summary>
    public sealed class SignalTowerHostedService : BackgroundService {
        private readonly ILogger<SignalTowerHostedService> _logger;
        private readonly SafeExecutor _safeExecutor;
        private readonly ISystemStateManager _systemStateManager;
        private readonly ICarrierManager _carrierManager;
        private readonly ISignalTower _signalTower;
        private readonly IOptions<LeadshaineIoPanelStateTransitionOptions> _options;
        private readonly object _buzzerLock = new();

        /// <summary>通用蜂鸣取消令牌源，任意新状态到来时重置（取消旧会话）。</summary>
        private CancellationTokenSource? _buzzerCts;

        /// <summary>蜂鸣代际号，每次新蜂鸣会话自增；用于防止旧任务关闭更新状态的蜂鸣。</summary>
        private int _buzzerGeneration;

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
            CancelBuzzer();
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
        /// 系统状态变更处理：先取消旧蜂鸣会话，再按新状态驱动灯光与蜂鸣。
        /// </summary>
        private void OnStateChanged(StateChangeEventArgs args) {
            // 步骤1：取消旧蜂鸣任务，获取新会话的代际号与取消令牌。
            var (gen, token) = StartNewBuzzerSession();

            // 步骤2：根据新状态驱动对应信号塔输出。
            switch (args.NewState) {
                case SystemState.Paused:
                case SystemState.Booting:
                case SystemState.Ready:
                    // 关灯并关闭蜂鸣。
                    _ = _safeExecutor.ExecuteAsync(async () => {
                        await _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Off).ConfigureAwait(false);
                        await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.Off).ConfigureAwait(false);
                    }, "SignalTower.SetLight.Off");
                    break;

                case SystemState.EmergencyStop:
                    _ = _safeExecutor.ExecuteAsync(async () => {
                        // 步骤a：亮红灯并开蜂鸣。
                        await _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Red).ConfigureAwait(false);
                        await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.On).ConfigureAwait(false);
                        try {
                            // 步骤b：等待 2 秒，可被新状态取消。
                            await Task.Delay(2000, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) {
                            // 被新状态取消，不关闭蜂鸣，由新状态决定蜂鸣。
                            return;
                        }
                        // 步骤c：仅当本代际仍为最新时关闭蜂鸣，防止覆盖新状态的蜂鸣。
                        if (Volatile.Read(ref _buzzerGeneration) == gen) {
                            await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.Off).ConfigureAwait(false);
                        }
                    }, "SignalTower.EmergencyStop");
                    break;

                case SystemState.StartupWarning:
                    _ = _safeExecutor.ExecuteAsync(
                        () => _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Yellow).AsTask(),
                        "SignalTower.SetLight.Yellow");
                    _ = _safeExecutor.ExecuteAsync(async () => {
                        // 步骤a：开蜂鸣。
                        await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.On).ConfigureAwait(false);
                        try {
                            // 步骤b：等待配置时长，可被新状态取消。
                            await Task.Delay(_options.Value.StartupWarningDurationMs, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) {
                            _logger.LogInformation("启动预警蜂鸣已被新状态取消，立即关闭蜂鸣器。");
                            return;
                        }
                        // 步骤c：仅当本代际仍为最新时关闭蜂鸣，防止覆盖新状态的蜂鸣。
                        if (Volatile.Read(ref _buzzerGeneration) == gen) {
                            await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.Off).ConfigureAwait(false);
                        }
                    }, "SignalTower.StartupWarningBuzzer");
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
        /// 取消旧蜂鸣会话并创建新会话，返回新代际号与取消令牌。
        /// </summary>
        private (int gen, CancellationToken token) StartNewBuzzerSession() {
            CancellationTokenSource? old;
            var newCts = new CancellationTokenSource();
            int gen;
            lock (_buzzerLock) {
                old = _buzzerCts;
                _buzzerCts = newCts;
                gen = Interlocked.Increment(ref _buzzerGeneration);
            }
            if (old is not null) {
                old.Cancel();
                old.Dispose();
            }
            return (gen, newCts.Token);
        }

        /// <summary>
        /// 取消当前活跃的蜂鸣会话（服务停止时调用）。
        /// </summary>
        private void CancelBuzzer() {
            CancellationTokenSource? cts;
            lock (_buzzerLock) {
                cts = _buzzerCts;
                _buzzerCts = null;
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
