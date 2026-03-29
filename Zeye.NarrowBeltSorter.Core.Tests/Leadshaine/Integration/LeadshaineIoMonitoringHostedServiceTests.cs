using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Options.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.IoPanel;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Sensor;
using Zeye.NarrowBeltSorter.Host.Services.Hosted;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Integration {
    /// <summary>
    /// Leadshaine IoMonitoringHostedService 编排测试。
    /// </summary>
    public sealed class LeadshaineIoMonitoringHostedServiceTests {
        /// <summary>
        /// 启动成功时应完成 EMC 初始化与点位下发。
        /// </summary>
        [Fact]
        public async Task StartAsync_WhenEmcInitialized_ShouldInitializeAndSetPoints() {
            var fakeEmc = new FakeLeadshaineEmcController {
                InitializeResult = true,
                SetMonitoredResult = true
            };
            var service = CreateService(fakeEmc);

            await service.StartAsync(CancellationToken.None);
            await Task.Delay(150);

            Assert.Equal(1, fakeEmc.InitializeCallCount);
            Assert.True(fakeEmc.SetMonitoredCallCount >= 1);
            Assert.Contains(fakeEmc.MonitoredPointBatches[0], batchPointId => batchPointId == "BTN-01");
            Assert.Contains(fakeEmc.MonitoredPointBatches[0], batchPointId => batchPointId == "I-01");

            await service.StopAsync(CancellationToken.None);
            Assert.True(fakeEmc.DisposeCalled);
        }

        /// <summary>
        /// EMC 初始化失败时不应继续下发点位。
        /// </summary>
        [Fact]
        public async Task StartAsync_WhenEmcInitializeFailed_ShouldNotSetPoints() {
            var fakeEmc = new FakeLeadshaineEmcController {
                InitializeResult = false
            };
            var service = CreateService(fakeEmc);

            await service.StartAsync(CancellationToken.None);
            await Task.Delay(120);

            Assert.Equal(1, fakeEmc.InitializeCallCount);
            Assert.Equal(0, fakeEmc.SetMonitoredCallCount);
            await service.StopAsync(CancellationToken.None);
        }

        /// <summary>
        /// 创建 IoMonitoringHostedService 测试实例。
        /// </summary>
        /// <param name="emcController">EMC 控制器测试桩。</param>
        /// <returns>托管服务实例。</returns>
        private static IoMonitoringHostedService CreateService(FakeLeadshaineEmcController emcController) {
            // 步骤1：构造点位绑定，确保 IoPanel 与 Sensor 引用点位可解析。
            var pointOptions = new LeadshainePointBindingCollectionOptions {
                Points = [
                    new LeadshainePointBindingOptions {
                        PointId = "BTN-01",
                        Binding = new LeadshaineBitBindingOptions {
                            Area = "Input",
                            CardNo = 0,
                            PortNo = 0,
                            BitIndex = 0,
                            TriggerState = "High"
                        }
                    },
                    new LeadshainePointBindingOptions {
                        PointId = "I-01",
                        Binding = new LeadshaineBitBindingOptions {
                            Area = "Input",
                            CardNo = 0,
                            PortNo = 0,
                            BitIndex = 1,
                            TriggerState = "High"
                        }
                    }
                ]
            };

            // 步骤2：构造 IoPanel 与 Sensor 绑定配置。
            var ioPanelOptions = new LeadshaineIoPanelButtonBindingCollectionOptions {
                Buttons = [
                    new LeadshaineIoPanelButtonBindingOptions {
                        ButtonName = "Emergency",
                        PointId = "BTN-01"
                    }
                ]
            };
            var sensorOptions = new LeadshaineSensorBindingCollectionOptions {
                Sensors = [
                    new LeadshaineSensorBindingOptions {
                        SensorName = "S1",
                        PointId = "I-01",
                        DebounceWindowMs = 0
                    }
                ]
            };

            // 步骤3：构造共享依赖并组装托管服务实例。
            var connectionOptions = new LeadshaineEmcConnectionOptions {
                PollingIntervalMs = 30
            };
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var ioPanelManager = new LeadshaineIoPanelManager(
                NullLogger<LeadshaineIoPanelManager>.Instance,
                safeExecutor,
                emcController,
                ioPanelOptions,
                pointOptions,
                connectionOptions);
            var sensorManager = new LeadshaineSensorManager(
                NullLogger<LeadshaineSensorManager>.Instance,
                safeExecutor,
                emcController,
                sensorOptions,
                pointOptions,
                connectionOptions);
            return new IoMonitoringHostedService(
                NullLogger<IoMonitoringHostedService>.Instance,
                emcController,
                ioPanelManager,
                sensorManager,
                Microsoft.Extensions.Options.Options.Create(ioPanelOptions),
                Microsoft.Extensions.Options.Options.Create(sensorOptions));
        }
    }
}
