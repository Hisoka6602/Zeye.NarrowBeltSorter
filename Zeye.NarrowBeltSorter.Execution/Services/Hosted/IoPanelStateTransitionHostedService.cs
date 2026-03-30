using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.IoPanel;
using Zeye.NarrowBeltSorter.Core.Manager.IoPanel;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Utilities;

namespace Zeye.NarrowBeltSorter.Execution.Services.Hosted {
    /// <summary>
    /// IoPanel 按钮到系统状态的桥接托管服务。
    /// </summary>
    public sealed class IoPanelStateTransitionHostedService : BackgroundService {
        private readonly ILogger<IoPanelStateTransitionHostedService> _logger;
        private readonly SafeExecutor _safeExecutor;
        private readonly IIoPanel _ioPanel;
        private readonly ISystemStateManager _systemStateManager;
        private EventHandler<IoPanelButtonPressedEventArgs>? _startHandler;
        private EventHandler<IoPanelButtonPressedEventArgs>? _stopHandler;
        private EventHandler<IoPanelButtonPressedEventArgs>? _emergencyPressedHandler;
        private EventHandler<IoPanelButtonPressedEventArgs>? _resetHandler;
        private EventHandler<IoPanelButtonReleasedEventArgs>? _emergencyReleasedHandler;

        public IoPanelStateTransitionHostedService(
            ILogger<IoPanelStateTransitionHostedService> logger,
            SafeExecutor safeExecutor,
            IIoPanel ioPanel,
            ISystemStateManager systemStateManager) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _ioPanel = ioPanel ?? throw new ArgumentNullException(nameof(ioPanel));
            _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            SubscribeButtons(stoppingToken);
            try {
                while (!stoppingToken.IsCancellationRequested) {
                    await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken).ConfigureAwait(false);
                }
            }
            finally {
                UnsubscribeButtons();
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken) {
            UnsubscribeButtons();
            return base.StopAsync(cancellationToken);
        }

        /// <summary>
        /// 订阅 IoPanel 各按钮事件，并将事件映射到系统状态切换。
        /// </summary>
        /// <param name="stoppingToken">服务停止令牌。</param>
        private void SubscribeButtons(CancellationToken stoppingToken) {
            _startHandler = (_, __) => _ = ChangeSystemStateSafeAsync(SystemState.Running, stoppingToken, "StartButtonPressed");
            _stopHandler = (_, __) => _ = ChangeSystemStateSafeAsync(SystemState.Paused, stoppingToken, "StopButtonPressed");
            _emergencyPressedHandler = (_, __) => _ = ChangeSystemStateSafeAsync(SystemState.EmergencyStop, stoppingToken, "EmergencyStopButtonPressed");
            _resetHandler = (_, __) => _ = ChangeSystemStateSafeAsync(SystemState.Booting, stoppingToken, "ResetButtonPressed");
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
