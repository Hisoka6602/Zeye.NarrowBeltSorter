using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Enums.Emc;
using Zeye.NarrowBeltSorter.Core.Options.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options;
using DriverPointBindingOptions = Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options.LeadshainePointBindingOptions;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Emc {
    /// <summary>
    /// Leadshaine EMC 控制器初始化测试。
    /// </summary>
    public sealed class LeadshaineEmcControllerInitializationTests {
        /// <summary>
        /// 初始化成功后应进入已连接状态并发布 Initialized 事件。
        /// </summary>
        [Fact]
        public async Task InitializeAsync_ShouldSetConnectedAndPublishInitialized() {
            var adapter = new FakeLeadshaineEmcHardwareAdapter();
            var controller = CreateController(adapter);
            var initializedTriggered = false;
            controller.Initialized += (_, _) => initializedTriggered = true;

            var result = await controller.InitializeAsync();

            Assert.True(result);
            Assert.Equal(EmcControllerStatus.Connected, controller.Status);
            Assert.True(initializedTriggered);
            await controller.DisposeAsync();
        }

        /// <summary>
        /// 初始化失败后应进入故障状态。
        /// </summary>
        [Fact]
        public async Task InitializeAsync_WhenBoardInitFailed_ShouldSetFaulted() {
            var adapter = new FakeLeadshaineEmcHardwareAdapter {
                InitializeCode = -1
            };
            var controller = CreateController(adapter);

            var result = await controller.InitializeAsync();

            Assert.False(result);
            Assert.Equal(EmcControllerStatus.Faulted, controller.Status);
            await controller.DisposeAsync();
        }

        /// <summary>
        /// 创建控制器实例。
        /// </summary>
        /// <param name="adapter">硬件适配器测试桩。</param>
        /// <returns>控制器实例。</returns>
        private static LeadshaineEmcController CreateController(FakeLeadshaineEmcHardwareAdapter adapter) {
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var connectionOptions = new LeadshaineEmcConnectionOptions {
                PollingIntervalMs = 50
            };
            var pointBindings = new LeadshainePointBindingCollectionOptions {
                Points = [
                    new DriverPointBindingOptions {
                        PointId = "I-01",
                        Binding = new LeadshaineBitBindingOptions {
                            Area = "Input",
                            CardNo = 0,
                            PortNo = 0,
                            BitIndex = 0,
                            TriggerState = "High"
                        }
                    }
                ]
            };

            return new LeadshaineEmcController(safeExecutor, connectionOptions, pointBindings, adapter);
        }
    }
}
