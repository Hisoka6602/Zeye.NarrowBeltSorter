using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Events.IoPanel;
using Zeye.NarrowBeltSorter.Core.Models.Emc;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc;
using System.Collections.Concurrent;
using Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Emc;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Integration {
    /// <summary>
    /// LeadshaineIoPanel 按钮边沿检测与角色事件路由测试。
    /// </summary>
    public sealed class LeadshaineIoPanelEdgeDetectionTests {
        /// <summary>
        /// 首次采样不应触发按下事件（防启动误触）。
        /// </summary>
        [Fact]
        public async Task StartMonitoringAsync_FirstSample_ShouldNotFirePressedEvent() {
            var fakeEmc = new FakeLeadshaineEmcController();
            fakeEmc.UpdatePoints([CreateInputPoint("BTN-01", value: true)]);

            var panel = CreatePanel(fakeEmc, "BTN-01", IoPanelButtonType.Start, "High");
            var events = new ConcurrentQueue<IoPanelButtonPressedEventArgs>();
            panel.StartButtonPressed += (_, args) => events.Enqueue(args);

            await panel.StartMonitoringAsync();
            await Task.Delay(80);

            Assert.Empty(events);
            await panel.StopMonitoringAsync();
            await panel.DisposeAsync();
        }

        /// <summary>
        /// 电平从 Low 到达 TriggerState=High 时应触发按下事件。
        /// </summary>
        [Fact]
        public async Task MonitoringLoop_WhenSignalRisesToTriggerState_ShouldFirePressedEvent() {
            var fakeEmc = new FakeLeadshaineEmcController();
            fakeEmc.UpdatePoints([CreateInputPoint("BTN-01", value: false)]);

            var panel = CreatePanel(fakeEmc, "BTN-01", IoPanelButtonType.Start, "High");
            var events = new ConcurrentQueue<IoPanelButtonPressedEventArgs>();
            panel.StartButtonPressed += (_, args) => events.Enqueue(args);

            await panel.StartMonitoringAsync();
            await Task.Delay(60);

            // 首次采样已记录 Low，现在切换到 High 应触发 pressed 边沿。
            fakeEmc.UpdatePoints([CreateInputPoint("BTN-01", value: true)]);
            await Task.Delay(60);

            Assert.Single(events);
            Assert.Equal("BTN-01", events.First().PointId);
            Assert.Equal(IoPanelButtonType.Start, events.First().ButtonType);
            await panel.StopMonitoringAsync();
            await panel.DisposeAsync();
        }

        /// <summary>
        /// TriggerState=Low 时，电平从 High 到 Low 应触发按下事件。
        /// </summary>
        [Fact]
        public async Task MonitoringLoop_WhenTriggerStateLow_ShouldFirePressedOnFallingEdge() {
            var fakeEmc = new FakeLeadshaineEmcController();
            fakeEmc.UpdatePoints([CreateInputPoint("BTN-01", value: true)]);

            var panel = CreatePanel(fakeEmc, "BTN-01", IoPanelButtonType.EmergencyStop, "Low");
            var events = new ConcurrentQueue<IoPanelButtonPressedEventArgs>();
            panel.EmergencyStopButtonPressed += (_, args) => events.Enqueue(args);

            await panel.StartMonitoringAsync();
            await Task.Delay(60);

            // 首次采样已记录 High，现在切换到 Low（=TriggerState）应触发 pressed 边沿。
            fakeEmc.UpdatePoints([CreateInputPoint("BTN-01", value: false)]);
            await Task.Delay(60);

            Assert.Single(events);
            Assert.Equal(IoPanelButtonType.EmergencyStop, events.First().ButtonType);
            await panel.StopMonitoringAsync();
            await panel.DisposeAsync();
        }

        /// <summary>
        /// 急停按钮释放（TriggerState=High，电平从 High 到 Low）应触发 EmergencyStopButtonReleased。
        /// </summary>
        [Fact]
        public async Task MonitoringLoop_WhenEmergencyStopReleased_ShouldFireReleasedEvent() {
            var fakeEmc = new FakeLeadshaineEmcController();
            fakeEmc.UpdatePoints([CreateInputPoint("BTN-ESTOP", value: false)]);

            var panel = CreatePanel(fakeEmc, "BTN-ESTOP", IoPanelButtonType.EmergencyStop, "High");
            var pressedEvents = new ConcurrentQueue<IoPanelButtonPressedEventArgs>();
            var releasedEvents = new ConcurrentQueue<IoPanelButtonReleasedEventArgs>();
            panel.EmergencyStopButtonPressed += (_, args) => pressedEvents.Enqueue(args);
            panel.EmergencyStopButtonReleased += (_, args) => releasedEvents.Enqueue(args);

            await panel.StartMonitoringAsync();
            await Task.Delay(60);

            // 按下（Low → High）。
            fakeEmc.UpdatePoints([CreateInputPoint("BTN-ESTOP", value: true)]);
            await Task.Delay(60);

            // 释放（High → Low）。
            fakeEmc.UpdatePoints([CreateInputPoint("BTN-ESTOP", value: false)]);
            await Task.Delay(60);

            Assert.Single(pressedEvents);
            Assert.Single(releasedEvents);
            Assert.Equal("BTN-ESTOP", releasedEvents.First().PointId);
            Assert.Equal(IoPanelButtonType.EmergencyStop, releasedEvents.First().ButtonType);
            await panel.StopMonitoringAsync();
            await panel.DisposeAsync();
        }

        /// <summary>
        /// 非急停按钮释放时不应触发 EmergencyStopButtonReleased。
        /// </summary>
        [Fact]
        public async Task MonitoringLoop_WhenNonEmergencyStopReleased_ShouldNotFireReleasedEvent() {
            var fakeEmc = new FakeLeadshaineEmcController();
            fakeEmc.UpdatePoints([CreateInputPoint("BTN-START", value: false)]);

            var panel = CreatePanel(fakeEmc, "BTN-START", IoPanelButtonType.Start, "High");
            var releasedEvents = new ConcurrentQueue<IoPanelButtonReleasedEventArgs>();
            panel.EmergencyStopButtonReleased += (_, args) => releasedEvents.Enqueue(args);

            await panel.StartMonitoringAsync();
            await Task.Delay(60);

            fakeEmc.UpdatePoints([CreateInputPoint("BTN-START", value: true)]);
            await Task.Delay(60);
            fakeEmc.UpdatePoints([CreateInputPoint("BTN-START", value: false)]);
            await Task.Delay(60);

            Assert.Empty(releasedEvents);
            await panel.StopMonitoringAsync();
            await panel.DisposeAsync();
        }

        /// <summary>
        /// Stop 按钮按下应路由到 StopButtonPressed，不触发 StartButtonPressed。
        /// </summary>
        [Fact]
        public async Task MonitoringLoop_StopButtonPressed_ShouldFireCorrectRoleEvent() {
            var fakeEmc = new FakeLeadshaineEmcController();
            fakeEmc.UpdatePoints([CreateInputPoint("BTN-STOP", value: false)]);

            var panel = CreatePanel(fakeEmc, "BTN-STOP", IoPanelButtonType.Stop, "High");
            var startEvents = new ConcurrentQueue<IoPanelButtonPressedEventArgs>();
            var stopEvents = new ConcurrentQueue<IoPanelButtonPressedEventArgs>();
            panel.StartButtonPressed += (_, args) => startEvents.Enqueue(args);
            panel.StopButtonPressed += (_, args) => stopEvents.Enqueue(args);

            await panel.StartMonitoringAsync();
            await Task.Delay(60);

            fakeEmc.UpdatePoints([CreateInputPoint("BTN-STOP", value: true)]);
            await Task.Delay(60);

            Assert.Empty(startEvents);
            Assert.Single(stopEvents);
            Assert.Equal(IoPanelButtonType.Stop, stopEvents.First().ButtonType);
            await panel.StopMonitoringAsync();
            await panel.DisposeAsync();
        }

        /// <summary>
        /// Faulted 状态下调用 StopMonitoringAsync 应正常取消后台循环并切换为 Stopped。
        /// </summary>
        [Fact]
        public async Task StopMonitoringAsync_WhenFaulted_ShouldStopBackgroundTask() {
            var fakeEmc = new FakeLeadshaineEmcController();
            fakeEmc.UpdatePoints([CreateInputPoint("BTN-01", value: false)]);

            var panel = CreatePanel(fakeEmc, "BTN-01", IoPanelButtonType.Start, "High");
            await panel.StartMonitoringAsync();
            await Task.Delay(60);

            // 通过 EMC 断链事件触发 Faulted 状态。
            fakeEmc.RaiseDisconnected();
            await Task.Delay(40);

            Assert.Equal(IoPanelMonitoringStatus.Faulted, panel.Status);

            // Faulted 时 StopMonitoringAsync 仍应能正常停止并切换到 Stopped。
            await panel.StopMonitoringAsync();

            Assert.Equal(IoPanelMonitoringStatus.Stopped, panel.Status);
            await panel.DisposeAsync();
        }

        /// <summary>
        /// Faulted 后重新调用 StartMonitoringAsync 应成功启动，不泄漏旧任务。
        /// </summary>
        [Fact]
        public async Task StartMonitoringAsync_AfterFaultedAndStopped_ShouldRestart() {
            var fakeEmc = new FakeLeadshaineEmcController();
            fakeEmc.UpdatePoints([CreateInputPoint("BTN-01", value: false)]);

            var panel = CreatePanel(fakeEmc, "BTN-01", IoPanelButtonType.Start, "High");
            await panel.StartMonitoringAsync();
            await Task.Delay(40);

            fakeEmc.RaiseDisconnected();
            await Task.Delay(40);

            Assert.Equal(IoPanelMonitoringStatus.Faulted, panel.Status);

            await panel.StopMonitoringAsync();
            await panel.StartMonitoringAsync();

            Assert.Equal(IoPanelMonitoringStatus.Monitoring, panel.Status);
            await panel.StopMonitoringAsync();
            await panel.DisposeAsync();
        }

        /// <summary>
        /// 创建 LeadshaineIoPanel 测试实例。
        /// </summary>
        private static LeadshaineIoPanel CreatePanel(
            FakeLeadshaineEmcController emcController,
            string pointId,
            IoPanelButtonType buttonType,
            string triggerState) {
            var connectionOptions = new LeadshaineEmcConnectionOptions { PollingIntervalMs = 20 };

            var buttonOptions = new LeadshaineIoPanelButtonBindingCollectionOptions {
                Buttons = [
                    new LeadshaineIoPanelButtonBindingOptions {
                        ButtonName = "TestButton",
                        ButtonType = buttonType,
                        PointId = pointId
                    }
                ]
            };

            var pointOptions = new LeadshaineIoPointBindingCollectionOptions {
                Points = [
                    new LeadshaineIoPointBindingOption {
                        PointId = pointId,
                        Binding = LeadshaineEmcControllerTestFactory.CreateBitBinding("Input", 0, 0, 0, triggerState)
                    }
                ]
            };

            return new LeadshaineIoPanel(
                NullLogger<LeadshaineIoPanel>.Instance,
                new SafeExecutor(NullLogger<SafeExecutor>.Instance),
                emcController,
                buttonOptions,
                pointOptions,
                connectionOptions);
        }

        /// <summary>
        /// 创建输入点位快照。
        /// </summary>
        private static IoPointInfo CreateInputPoint(string pointId, bool value) {
            return new IoPointInfo {
                PointId = pointId,
                Area = "Input",
                CardNo = 0,
                PortNo = 0,
                BitIndex = 0,
                Value = value,
                CapturedAt = DateTime.Now
            };
        }
    }
}
