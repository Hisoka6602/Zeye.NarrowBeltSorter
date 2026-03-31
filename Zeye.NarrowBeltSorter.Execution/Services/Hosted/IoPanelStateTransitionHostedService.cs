using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.IoPanel;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Manager.IoPanel;
namespace Zeye.NarrowBeltSorter.Execution.Services.Hosted {

    /// <summary>
    /// IoPanel 按钮到系统状态的桥接托管服务。
    /// </summary>
    public sealed class IoPanelStateTransitionHostedService : BackgroundService {
        private static readonly TimeSpan DefaultStartupWarningDuration = TimeSpan.FromSeconds(3);
        private readonly ILogger<IoPanelStateTransitionHostedService> _logger;
        private readonly SafeExecutor _safeExecutor;
        private readonly IIoPanel _ioPanel;
        private readonly ISystemStateManager _systemStateManager;
        private readonly TimeSpan _startupWarningDuration;
        private readonly object _startupTransitionSyncRoot = new();
        private EventHandler<IoPanelButtonPressedEventArgs>? _startHandler;
        private EventHandler<IoPanelButtonPressedEventArgs>? _stopHandler;
        private EventHandler<IoPanelButtonPressedEventArgs>? _emergencyPressedHandler;
        private EventHandler<IoPanelButtonPressedEventArgs>? _resetHandler;
        private EventHandler<IoPanelButtonReleasedEventArgs>? _emergencyReleasedHandler;
        private CancellationTokenSource? _startupTransitionCts;

        public IoPanelStateTransitionHostedService(
            ILogger<IoPanelStateTransitionHostedService> logger,
            SafeExecutor safeExecutor,
            IIoPanel ioPanel,
            ISystemStateManager systemStateManager,
            TimeSpan? startupWarningDuration = null) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _ioPanel = ioPanel ?? throw new ArgumentNullException(nameof(ioPanel));
            _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));
            _startupWarningDuration = startupWarningDuration ?? DefaultStartupWarningDuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            SubscribeButtons(stoppingToken);
            try {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // 宿主正常停止，退出保活等待。
            }
            finally {
                UnsubscribeButtons();
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken) {
            CancelStartupTransition("ServiceStop");
            UnsubscribeButtons();
            return base.StopAsync(cancellationToken);
        }

        /// <summary>
        /// 订阅 IoPanel 各按钮事件，并将事件映射到系统状态切换。
        /// </summary>
        /// <param name="stoppingToken">服务停止令牌。</param>
        private void SubscribeButtons(CancellationToken stoppingToken) {
            _startHandler = (_, __) => BeginStartupTransition(stoppingToken);
            _stopHandler = (_, __) => {
                CancelStartupTransition("StopButtonPressed");
                _ = ChangeSystemStateSafeAsync(SystemState.Paused, stoppingToken, "StopButtonPressed");
            };
            _emergencyPressedHandler = (_, __) => {
                CancelStartupTransition("EmergencyStopButtonPressed");
                _ = ChangeSystemStateSafeAsync(SystemState.EmergencyStop, stoppingToken, "EmergencyStopButtonPressed");
            };
            _resetHandler = (_, __) => {
                CancelStartupTransition("ResetButtonPressed");
                _ = ChangeSystemStateSafeAsync(SystemState.Booting, stoppingToken, "ResetButtonPressed");
            };
            _emergencyReleasedHandler = (_, __) => _ = ChangeSystemStateSafeAsync(SystemState.Ready, stoppingToken, "EmergencyStopButtonReleased");

            _ioPanel.StartButtonPressed += _startHandler;
            _ioPanel.StopButtonPressed += _stopHandler;
            _ioPanel.EmergencyStopButtonPressed += _emergencyPressedHandler;
            _ioPanel.ResetButtonPressed += _resetHandler;
            _ioPanel.EmergencyStopButtonReleased += _emergencyReleasedHandler;
            _logger.LogInformation("IoPanelStateTransitionHostedService 已挂载按钮状态桥接。");
        }

        /// <summary>
        /// 取消订阅 IoPanel 所有按钮事件，并释放事件委托引用。
        /// </summary>
        private void UnsubscribeButtons() {
            if (_startHandler is not null) {
                _ioPanel.StartButtonPressed -= _startHandler;
                _startHandler = null;
            }

            if (_stopHandler is not null) {
                _ioPanel.StopButtonPressed -= _stopHandler;
                _stopHandler = null;
            }

            if (_emergencyPressedHandler is not null) {
                _ioPanel.EmergencyStopButtonPressed -= _emergencyPressedHandler;
                _emergencyPressedHandler = null;
            }

            if (_resetHandler is not null) {
                _ioPanel.ResetButtonPressed -= _resetHandler;
                _resetHandler = null;
            }

            if (_emergencyReleasedHandler is not null) {
                _ioPanel.EmergencyStopButtonReleased -= _emergencyReleasedHandler;
                _emergencyReleasedHandler = null;
            }
        }

        private void BeginStartupTransition(CancellationToken stoppingToken) {
            var startupTransitionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            lock (_startupTransitionSyncRoot) {
                CancelStartupTransitionUnsafe("RestartByStart");
                _startupTransitionCts = startupTransitionCts;
                _ = RunStartupTransitionAsync(startupTransitionCts);
            }
        }

        private async Task RunStartupTransitionAsync(CancellationTokenSource startupTransitionCts) {
            try {
                await ChangeSystemStateSafeAsync(SystemState.StartupWarning, startupTransitionCts.Token, "StartButtonPressed.StartupWarning").ConfigureAwait(false);
                await Task.Delay(_startupWarningDuration, startupTransitionCts.Token).ConfigureAwait(false);
                await ChangeSystemStateSafeAsync(SystemState.Running, startupTransitionCts.Token, "StartButtonPressed.Running").ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (startupTransitionCts.IsCancellationRequested) {
                _logger.LogInformation("启动预警阶段已取消，阻止切换到 Running。");
            }
            finally {
                lock (_startupTransitionSyncRoot) {
                    if (ReferenceEquals(_startupTransitionCts, startupTransitionCts)) {
                        _startupTransitionCts = null;
                    }
                }

                startupTransitionCts.Dispose();
            }
        }

        private void CancelStartupTransition(string reason) {
            lock (_startupTransitionSyncRoot) {
                CancelStartupTransitionUnsafe(reason);
            }
        }

        private void CancelStartupTransitionUnsafe(string reason) {
            if (_startupTransitionCts is null) {
                return;
            }

            _logger.LogInformation("取消启动预警阶段：Reason={Reason}。", reason);
            _startupTransitionCts.Cancel();
        }

        private Task ChangeSystemStateSafeAsync(
            SystemState targetState,
            CancellationToken stoppingToken,
            string sourceEvent) {
            return _safeExecutor.ExecuteAsync(
                async token => {
                    var changed = await _systemStateManager.ChangeStateAsync(targetState, token).ConfigureAwait(false);
                    if (!changed) {
                        _logger.LogWarning("IoPanel 状态桥接未生效：Event={Event}, TargetState={TargetState}。", sourceEvent, targetState);
                    }
                },
                $"IoPanelStateTransitionHostedService.{sourceEvent}",
                stoppingToken);
        }
    }
}
