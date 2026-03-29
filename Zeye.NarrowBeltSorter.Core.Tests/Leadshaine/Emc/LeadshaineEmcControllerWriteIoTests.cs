using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Options.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options;
using DriverPointBindingOptions = Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options.LeadshainePointBindingOptions;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Emc {
    /// <summary>
    /// Leadshaine EMC 控制器写入测试。
    /// </summary>
    public sealed class LeadshaineEmcControllerWriteIoTests {
        /// <summary>
        /// 写输出点应调用 dmc_write_outbit。
        /// </summary>
        [Fact]
        public async Task WriteIoAsync_WhenOutputPoint_ShouldWriteOutBit() {
            var adapter = new FakeLeadshaineEmcHardwareAdapter();
            var controller = CreateController(adapter);

            var result = await controller.WriteIoAsync("Q-01", true);

            Assert.True(result);
            Assert.NotNull(adapter.LastWriteOutBit);
            Assert.Equal((ushort)0, adapter.LastWriteOutBit.Value.CardNo);
            Assert.Equal((ushort)3, adapter.LastWriteOutBit.Value.BitNo);
            Assert.Equal((ushort)1, adapter.LastWriteOutBit.Value.OnOff);
            await controller.DisposeAsync();
        }

        /// <summary>
        /// 写输入点应被拒绝。
        /// </summary>
        [Fact]
        public async Task WriteIoAsync_WhenInputPoint_ShouldReturnFalse() {
            var adapter = new FakeLeadshaineEmcHardwareAdapter();
            var controller = CreateController(adapter);

            var result = await controller.WriteIoAsync("I-01", true);

            Assert.False(result);
            Assert.Null(adapter.LastWriteOutBit);
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
                            BitIndex = 1,
                            TriggerState = "High"
                        }
                    },
                    new DriverPointBindingOptions {
                        PointId = "Q-01",
                        Binding = new LeadshaineBitBindingOptions {
                            Area = "Output",
                            CardNo = 0,
                            PortNo = 0,
                            BitIndex = 3,
                            TriggerState = "High"
                        }
                    }
                ]
            };

            return new LeadshaineEmcController(safeExecutor, connectionOptions, pointBindings, adapter);
        }
    }
}
