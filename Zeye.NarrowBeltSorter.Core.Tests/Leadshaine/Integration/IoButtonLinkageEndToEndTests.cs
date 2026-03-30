using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.IoPanel;
using Zeye.NarrowBeltSorter.Core.Manager.IoPanel;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Execution.Services.Hosted;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Integration {
    /// <summary>
    /// IO 按钮 -> 系统状态 -> IO 联动端到端测试。
    /// </summary>
    public sealed class IoButtonLinkageEndToEndTests {
        [Fact]
        public async Task StartButtonPressed_ShouldTriggerRunningLinkageWrite() {
            var ioPanel = new FakeIoPanel();
            var stateManager = new FakeSystemStateManager();
            var emc = new FakeLeadshaineEmcController();
            var transitionService = new IoPanelStateTransitionHostedService(
                NullLogger<IoPanelStateTransitionHostedService>.Instance,
                new SafeExecutor(NullLogger<SafeExecutor>.Instance),
                ioPanel,
                stateManager);
            var linkageService = new IoLinkageHostedService(
                NullLogger<IoLinkageHostedService>.Instance,
                stateManager,
                emc,
                Microsoft.Extensions.Options.Options.Create(new LeadshaineIoLinkageOptions {
                    Enabled = true,
                    Points = [
                        new LeadshaineIoLinkagePointOptions {
                            RelatedSystemState = SystemState.Running,
                            PointId = "Q-Run",
                            TriggerValue = true,
                            DelayMs = 0,
                            DurationMs = 0
                        }
                    ]
                }));

            await linkageService.StartAsync(CancellationToken.None);
            Assert.True(stateManager.WaitForSubscriber(200));
            await transitionService.StartAsync(CancellationToken.None);

            ioPanel.RaisePressed(IoPanelButtonType.Start);
            Assert.True(emc.WaitForWriteCount(1, 1000));

            await transitionService.StopAsync(CancellationToken.None);
            await linkageService.StopAsync(CancellationToken.None);
            Assert.Contains(emc.WriteIoCalls, x => x.PointId == "Q-Run" && x.Value);
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
        }
    }
}
