using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.Io;
using Zeye.NarrowBeltSorter.Core.Events.System;
using Zeye.NarrowBeltSorter.Core.Manager.Sensor;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Enums.SignalTower;
using Zeye.NarrowBeltSorter.Core.Manager.SignalTower;

namespace Zeye.NarrowBeltSorter.Execution.Services.Hosted {

    /// <summary>
    /// 检修服务：监听检修开关传感器状态，驱动系统在运行状态与检修状态之间切换，
    /// 并阻止在检修开关打开期间进入运行状态。急停状态下检修开关无法生效，会触发 5 秒蜂鸣警告。
    /// </summary>
    public sealed class MaintenanceHostedService : BackgroundService {

        /// <summary>检修切换前等待停止完成的延迟时长（毫秒）。</summary>
        private const int StopToMaintenanceDelayMs = 300;

        /// <summary>急停状态下触发检修开关时的蜂鸣警告持续时长（毫秒）。</summary>
        private const int EmergencyStopWarningBuzzerMs = 5000;

        private readonly ILogger<MaintenanceHostedService> _logger;
        private readonly SafeExecutor _safeExecutor;
        private readonly ISensorManager _sensorManager;
        private readonly ISystemStateManager _systemStateManager;
        private readonly IOptionsMonitor<LoopTrackServiceOptions> _loopTrackOptions;

        /// <summary>信号塔接口（可选，未注册时为 null，跳过蜂鸣器操作）。</summary>
        private readonly ISignalTower? _signalTower;

        /// <summary>检修开关是否处于打开状态（volatile 保证多线程可见性）。</summary>
        private volatile bool _maintenanceSwitchOpen;

        /// <summary>保护检修切换 CTS 替换的同步锁。</summary>
        private readonly object _switchChangeLock = new();

        /// <summary>当前检修开关切换任务的取消令牌源；每次新事件到来时取消前一轮。</summary>
        private CancellationTokenSource? _switchChangeCts;

        private EventHandler<SensorStateChangedEventArgs>? _sensorStateChangedHandler;
        private EventHandler<StateChangeEventArgs>? _stateChangedHandler;

        /// <summary>
        /// 初始化检修服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <param name="sensorManager">传感器管理器。</param>
        /// <param name="systemStateManager">系统状态管理器。</param>
        /// <param name="loopTrackOptions">环轨配置（含检修速度）。</param>
        /// <param name="serviceProvider">服务提供者（用于可选解析信号塔接口）。</param>
        public MaintenanceHostedService(
            ILogger<MaintenanceHostedService> logger,
            SafeExecutor safeExecutor,
            ISensorManager sensorManager,
            ISystemStateManager systemStateManager,
            IOptionsMonitor<LoopTrackServiceOptions> loopTrackOptions,
            IServiceProvider serviceProvider) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _sensorManager = sensorManager ?? throw new ArgumentNullException(nameof(sensorManager));
            _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));
            _loopTrackOptions = loopTrackOptions ?? throw new ArgumentNullException(nameof(loopTrackOptions));
            if (serviceProvider is null) {
                throw new ArgumentNullException(nameof(serviceProvider));
            }
            _signalTower = serviceProvider.GetService<ISignalTower>();
        }

        /// <summary>
        /// 订阅传感器状态变更事件与系统状态变更事件，保持服务存活直至停止信号。
        /// </summary>
        /// <param name="stoppingToken">服务停止令牌。</param>
        /// <returns>异步任务。</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            SubscribeEvents(stoppingToken);
            _logger.LogInformation("MaintenanceHostedService 已启动，等待检修开关传感器事件。");
            try {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // 宿主正常停止，退出保活等待。
            }
            finally {
                UnsubscribeEvents();
                _logger.LogInformation("MaintenanceHostedService 已停止。");
            }
        }

        /// <summary>
        /// 订阅传感器状态变更事件与系统状态变更事件。
        /// </summary>
        /// <param name="stoppingToken">服务停止令牌。</param>
        private void SubscribeEvents(CancellationToken stoppingToken) {
            _sensorStateChangedHandler = (_, args) => {
                if (args.SensorType != IoPointType.MaintenanceSwitchSensor) {
                    return;
                }

                var isOpen = args.NewState == IoState.High;
                CancellationTokenSource currentCts;
                CancellationTokenSource? previousCts;

                // 步骤1：在同一临界区内替换当前轮取消令牌，并取出上一轮实例，避免旧任务延迟后覆盖新状态。
                lock (_switchChangeLock) {
                    previousCts = _switchChangeCts;
                    currentCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    _switchChangeCts = currentCts;
                }

                // 步骤2：取消并释放上一轮未完成的检修切换。
                previousCts?.Cancel();
                previousCts?.Dispose();

                _ = _safeExecutor.ExecuteAsync(
                    token => new ValueTask(OnMaintenanceSwitchChangedAsync(isOpen, token)),
                    "MaintenanceHostedService.SensorStateChanged",
                    currentCts.Token);
            };

            _stateChangedHandler = (_, args) => {
                if (args.NewState == SystemState.Running && _maintenanceSwitchOpen) {
                    _logger.LogWarning(
                        "MaintenanceHostedService 检测到检修开关打开时尝试进入运行状态，立即驳回并切换回检修状态 OldState={OldState}。",
                        args.OldState);
                    _ = _safeExecutor.ExecuteAsync(
                        token => new ValueTask(_systemStateManager.ChangeStateAsync(SystemState.Maintenance, token)),
                        "MaintenanceHostedService.BlockRunning",
                        stoppingToken);
                }
            };

            _sensorManager.SensorStateChanged += _sensorStateChangedHandler;
            _systemStateManager.StateChanged += _stateChangedHandler;
        }

        /// <summary>
        /// 退订所有已订阅事件，并取消/释放挂起的切换任务。
        /// </summary>
        private void UnsubscribeEvents() {
            // 步骤1：先退订事件，确保在清理 CTS 时不会有新事件产生新 CTS 实例。
            if (_sensorStateChangedHandler is not null) {
                _sensorManager.SensorStateChanged -= _sensorStateChangedHandler;
                _sensorStateChangedHandler = null;
            }

            if (_stateChangedHandler is not null) {
                _systemStateManager.StateChanged -= _stateChangedHandler;
                _stateChangedHandler = null;
            }

            // 步骤2：退订事件之后再清理 CTS，保证不再有新事件创建新 CTS 实例。
            CancellationTokenSource? lastCts;
            lock (_switchChangeLock) {
                lastCts = _switchChangeCts;
                _switchChangeCts = null;
            }
            lastCts?.Cancel();
            lastCts?.Dispose();
        }

        /// <summary>
        /// 处理检修开关状态变化：打开时进入检修状态并启动轨道，关闭时停止轨道并切换为暂停状态。
        /// </summary>
        /// <param name="isOpen">检修开关是否打开（true=打开，false=关闭）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task OnMaintenanceSwitchChangedAsync(bool isOpen, CancellationToken cancellationToken) {
            _maintenanceSwitchOpen = isOpen;
            if (isOpen) {
                await EnterMaintenanceAsync(cancellationToken).ConfigureAwait(false);
            }
            else {
                await ExitMaintenanceAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 进入检修状态：急停状态下拒绝切换并触发 5 秒蜂鸣警告；
        /// 运行态先暂停并等待 300ms；随后切换为检修状态。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task EnterMaintenanceAsync(CancellationToken cancellationToken) {
            var currentState = _systemStateManager.CurrentState;
            var maintenanceSpeedMmps = _loopTrackOptions.CurrentValue.MaintenanceTargetSpeedMmps;

            // 步骤0：急停状态下禁止进入检修状态，触发 5 秒蜂鸣警告后返回。
            if (currentState == SystemState.EmergencyStop) {
                _logger.LogWarning(
                    "MaintenanceHostedService 急停状态下检修开关被触发，拒绝进入检修状态，触发 {BuzzerMs}ms 蜂鸣警告。",
                    EmergencyStopWarningBuzzerMs);
                _ = _safeExecutor.ExecuteAsync(async () => {
                    if (_signalTower is null) {
                        return;
                    }
                    await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.On).ConfigureAwait(false);
                    try {
                        await Task.Delay(EmergencyStopWarningBuzzerMs, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) {
                        _logger.LogInformation("MaintenanceHostedService 急停检修蜂鸣警告被提前中止。");
                    }
                    await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.Off).ConfigureAwait(false);
                }, "MaintenanceHostedService.EmergencyStopWarningBuzzer");
                return;
            }

            _logger.LogInformation(
                "MaintenanceHostedService 检修开关已打开，准备进入检修状态 CurrentState={CurrentState} MaintenanceSpeedMmps={MaintenanceSpeedMmps}。",
                currentState,
                maintenanceSpeedMmps);

            // 步骤1：若当前处于运行态，先切换为暂停，等待轨道停止稳定。
            if (currentState == SystemState.Running) {
                var paused = await _systemStateManager.ChangeStateAsync(SystemState.Paused, cancellationToken).ConfigureAwait(false);
                if (!paused) {
                    _logger.LogError("MaintenanceHostedService 切换为暂停状态失败，无法继续进入检修状态。");
                    return;
                }
                _logger.LogInformation("MaintenanceHostedService 已从运行状态切换为暂停状态，等待 {DelayMs}ms 后进入检修状态。", StopToMaintenanceDelayMs);
                try {
                    await Task.Delay(StopToMaintenanceDelayMs, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    _logger.LogWarning("MaintenanceHostedService 等待进入检修状态期间被取消。");
                    return;
                }
            }

            // 步骤2：切换系统状态为检修。
            var maintenance = await _systemStateManager.ChangeStateAsync(SystemState.Maintenance, cancellationToken).ConfigureAwait(false);
            if (!maintenance) {
                _logger.LogError(
                    "MaintenanceHostedService 切换检修状态失败 CurrentState={CurrentState}。",
                    _systemStateManager.CurrentState);
                return;
            }

            _logger.LogInformation(
                "MaintenanceHostedService 已进入检修状态，检修轨道目标速度={MaintenanceSpeedMmps}mm/s（由 LoopTrackManagerHostedService 负责启动轨道）。",
                maintenanceSpeedMmps);
        }

        /// <summary>
        /// 退出检修状态：切换系统状态为暂停（LoopTrackManagerHostedService 负责停止轨道）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task ExitMaintenanceAsync(CancellationToken cancellationToken) {
            _logger.LogInformation(
                "MaintenanceHostedService 检修开关已关闭，退出检修状态 CurrentState={CurrentState}。",
                _systemStateManager.CurrentState);

            // 切换系统状态为暂停；LoopTrackManagerHostedService 会因非运行态而停止轨道。
            var paused = await _systemStateManager.ChangeStateAsync(SystemState.Paused, cancellationToken).ConfigureAwait(false);
            if (!paused) {
                _logger.LogError(
                    "MaintenanceHostedService 退出检修状态切换为暂停失败 CurrentState={CurrentState}。",
                    _systemStateManager.CurrentState);
                return;
            }

            _logger.LogInformation("MaintenanceHostedService 已退出检修状态，系统切换为暂停状态。");
        }
    }
}
