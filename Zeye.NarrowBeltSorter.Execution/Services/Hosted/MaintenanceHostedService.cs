using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.Io;
using Zeye.NarrowBeltSorter.Core.Events.System;
using Zeye.NarrowBeltSorter.Core.Manager.Sensor;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;

namespace Zeye.NarrowBeltSorter.Execution.Services.Hosted {

    /// <summary>
    /// 检修服务：监听检修开关传感器状态，驱动系统在运行状态与检修状态之间切换，
    /// 并阻止在检修开关打开期间进入运行状态。
    /// </summary>
    public sealed class MaintenanceHostedService : BackgroundService {

        /// <summary>检修切换前等待停止完成的延迟时长（毫秒）。</summary>
        private const int StopToMaintenanceDelayMs = 300;

        private readonly ILogger<MaintenanceHostedService> _logger;
        private readonly SafeExecutor _safeExecutor;
        private readonly ISensorManager _sensorManager;
        private readonly ISystemStateManager _systemStateManager;
        private readonly IOptionsMonitor<LoopTrackServiceOptions> _loopTrackOptions;

        /// <summary>检修开关是否处于打开状态（volatile 保证多线程可见性）。</summary>
        private volatile bool _maintenanceSwitchOpen;

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
        public MaintenanceHostedService(
            ILogger<MaintenanceHostedService> logger,
            SafeExecutor safeExecutor,
            ISensorManager sensorManager,
            ISystemStateManager systemStateManager,
            IOptionsMonitor<LoopTrackServiceOptions> loopTrackOptions) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _sensorManager = sensorManager ?? throw new ArgumentNullException(nameof(sensorManager));
            _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));
            _loopTrackOptions = loopTrackOptions ?? throw new ArgumentNullException(nameof(loopTrackOptions));
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
                _ = _safeExecutor.ExecuteAsync(
                    token => new ValueTask(OnMaintenanceSwitchChangedAsync(isOpen, token)),
                    "MaintenanceHostedService.SensorStateChanged",
                    stoppingToken);
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
        /// 退订所有已订阅事件。
        /// </summary>
        private void UnsubscribeEvents() {
            if (_sensorStateChangedHandler is not null) {
                _sensorManager.SensorStateChanged -= _sensorStateChangedHandler;
                _sensorStateChangedHandler = null;
            }

            if (_stateChangedHandler is not null) {
                _systemStateManager.StateChanged -= _stateChangedHandler;
                _stateChangedHandler = null;
            }
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
        /// 进入检修状态：若当前为运行态则先暂停并等待 300ms，再切换为检修状态。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task EnterMaintenanceAsync(CancellationToken cancellationToken) {
            var maintenanceSpeedMmps = _loopTrackOptions.CurrentValue.MaintenanceTargetSpeedMmps;
            _logger.LogInformation(
                "MaintenanceHostedService 检修开关已打开，准备进入检修状态 CurrentState={CurrentState} MaintenanceSpeedMmps={MaintenanceSpeedMmps}。",
                _systemStateManager.CurrentState,
                maintenanceSpeedMmps);

            // 步骤1：若当前处于运行态，先切换为暂停，等待轨道停止稳定。
            if (_systemStateManager.CurrentState == SystemState.Running) {
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
