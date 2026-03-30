using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.IoPanel;
using Zeye.NarrowBeltSorter.Core.Events.System;
using Zeye.NarrowBeltSorter.Core.Manager.IoPanel;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Execution.Services.Hosted;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Integration {
    /// <summary>
    /// IoPanelStateTransitionHostedService 行为测试。
    /// </summary>
    public sealed class IoPanelStateTransitionHostedServiceTests {
        [Theory]
        [InlineData(IoPanelButtonType.Start, SystemState.Running)]
        [InlineData(IoPanelButtonType.Stop, SystemState.Paused)]
        [InlineData(IoPanelButtonType.EmergencyStop, SystemState.EmergencyStop)]
        [InlineData(IoPanelButtonType.Reset, SystemState.Booting)]
        public async Task ButtonPressed_ShouldChangeState(IoPanelButtonType buttonType, SystemState expectedState) {
            var ioPanel = new FakeIoPanel();
            var stateManager = new TrackingSystemStateManager();
            var service = CreateService(ioPanel, stateManager);

            await service.StartAsync(CancellationToken.None);
            ioPanel.RaisePressed(buttonType);
            await Task.Delay(80);

            Assert.Contains(expectedState, stateManager.ChangedStates);
            await service.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task EmergencyReleased_ShouldChangeStateToReady() {
            var ioPanel = new FakeIoPanel();
            var stateManager = new TrackingSystemStateManager();
            var service = CreateService(ioPanel, stateManager);

            await service.StartAsync(CancellationToken.None);
            ioPanel.RaiseReleased(IoPanelButtonType.EmergencyStop);
            await Task.Delay(80);

            Assert.Contains(SystemState.Ready, stateManager.ChangedStates);
            await service.StopAsync(CancellationToken.None);
        }

        private static IoPanelStateTransitionHostedService CreateService(FakeIoPanel ioPanel, TrackingSystemStateManager stateManager) {
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            return new IoPanelStateTransitionHostedService(
                NullLogger<IoPanelStateTransitionHostedService>.Instance,
                safeExecutor,
                ioPanel,
                stateManager);
        }

        private sealed class FakeIoPanel : IIoPanel {
            public IoPanelMonitoringStatus Status => IoPanelMonitoringStatus.Monitoring;
            public bool IsMonitoring => true;
            public IReadOnlyCollection<string> MonitoredPointIds => [];
            public event EventHandler<IoPanelButtonPressedEventArgs>? StartButtonPressed;
            public event EventHandler<IoPanelButtonPressedEventArgs>? StopButtonPressed;
            public event EventHandler<IoPanelButtonPressedEventArgs>? EmergencyStopButtonPressed;
            public event EventHandler<IoPanelButtonPressedEventArgs>? ResetButtonPressed;
            public event EventHandler<IoPanelButtonReleasedEventArgs>? EmergencyStopButtonReleased;
            public event EventHandler<IoPanelMonitoringStatusChangedEventArgs>? MonitoringStatusChanged;
            public event EventHandler<IoPanelFaultedEventArgs>? Faulted;

            public ValueTask StartMonitoringAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
            public ValueTask StopMonitoringAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

            public void RaisePressed(IoPanelButtonType buttonType) {
                var args = new IoPanelButtonPressedEventArgs("P1", "BTN", buttonType, DateTime.Now);
                switch (buttonType) {
                    case IoPanelButtonType.Start:
                        StartButtonPressed?.Invoke(this, args);
                        break;
                    case IoPanelButtonType.Stop:
                        StopButtonPressed?.Invoke(this, args);
                        break;
                    case IoPanelButtonType.EmergencyStop:
                        EmergencyStopButtonPressed?.Invoke(this, args);
                        break;
                    case IoPanelButtonType.Reset:
                        ResetButtonPressed?.Invoke(this, args);
                        break;
                }
            }

            public void RaiseReleased(IoPanelButtonType buttonType) {
                var args = new IoPanelButtonReleasedEventArgs("P1", "BTN", buttonType, DateTime.Now);
                EmergencyStopButtonReleased?.Invoke(this, args);
            }
        }

        private sealed class TrackingSystemStateManager : ISystemStateManager {
            public SystemState CurrentState { get; private set; } = SystemState.Ready;
            public List<SystemState> ChangedStates { get; } = [];
            public event EventHandler<StateChangeEventArgs>? StateChanged;

            public Task<bool> ChangeStateAsync(SystemState targetState, CancellationToken cancellationToken = default) {
                cancellationToken.ThrowIfCancellationRequested();
                var oldState = CurrentState;
                CurrentState = targetState;
                ChangedStates.Add(targetState);
                StateChanged?.Invoke(this, new StateChangeEventArgs(oldState, targetState, DateTime.Now));
                return Task.FromResult(true);
            }

            public void Dispose() {
            }
        }
    }
}
