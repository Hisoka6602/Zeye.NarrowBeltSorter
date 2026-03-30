using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc.Options;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Sensor;
using Zeye.NarrowBeltSorter.Execution.Services.Hosted;

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
            // IoPanel 与 Sensor 分别触发点位同步，允许分批下发；此处验证最终下发全集是否完整。
            var mergedPointIds = fakeEmc.MonitoredPointBatches
                .SelectMany(static batch => batch)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Assert.Contains("BTN-01", mergedPointIds, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("I-01", mergedPointIds, StringComparer.OrdinalIgnoreCase);

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
        /// 点位下发失败时应回收资源并释放 EMC。
        /// </summary>
        [Fact]
        public async Task StartAsync_WhenSetMonitoredFailed_ShouldCleanupAndDisposeEmc() {
            var fakeEmc = new FakeLeadshaineEmcController {
                InitializeResult = true,
                SetMonitoredResult = false
            };
            var service = CreateService(fakeEmc);

            await service.StartAsync(CancellationToken.None);
            await Task.Delay(120);

            Assert.Equal(1, fakeEmc.InitializeCallCount);
            Assert.Equal(1, fakeEmc.SetMonitoredCallCount);
            Assert.True(fakeEmc.DisposeCalled);
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
            var corePointOptions = new LeadshaineIoPointBindingCollectionOptions {
                Points = pointOptions.Points.Select(static x => new LeadshaineIoPointBindingOption {
                    PointId = x.PointId,
                    Binding = new LeadshaineBitBindingOption {
                        Area = x.Binding.Area,
                        CardNo = x.Binding.CardNo,
                        PortNo = x.Binding.PortNo,
                        BitIndex = x.Binding.BitIndex,
                        TriggerState = x.Binding.TriggerState
                    }
                }).ToList()
            };

            // 步骤2：构造 IoPanel 与 Sensor 绑定配置。
            var ioPanelOptions = new LeadshaineIoPanelButtonBindingCollectionOptions {
                Buttons = [
                    new LeadshaineIoPanelButtonBindingOptions {
                        ButtonName = "Emergency",
                        ButtonType = IoPanelButtonType.EmergencyStop,
                        PointId = "BTN-01"
                    }
                ]
            };
            var sensorOptions = new LeadshaineSensorBindingCollectionOptions {
                Sensors = [
                    new LeadshaineSensorBindingOptions {
                        SensorName = "S1",
                        SensorType = IoPointType.ParcelCreateSensor,
                        PointId = "I-01",
                        PollIntervalMs = 10,
                        DebounceWindowMs = 0
                    }
                ]
            };

            // 步骤3：构造共享依赖并组装托管服务实例。
            var connectionOptions = new LeadshaineEmcConnectionOptions {
                PollingIntervalMs = 30
            };
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var ioPanelManager = new LeadshaineIoPanel(
                NullLogger<LeadshaineIoPanel>.Instance,
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
                corePointOptions);
        }
    }
}
