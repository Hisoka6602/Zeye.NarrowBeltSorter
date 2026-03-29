using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Enums.Emc;
using Zeye.NarrowBeltSorter.Core.Options.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Emc {
    /// <summary>
    /// Leadshaine EMC 控制器重连测试。
    /// </summary>
    public sealed class LeadshaineEmcControllerReconnectTests {
        /// <summary>
        /// 重连成功后应恢复为已连接状态。
        /// </summary>
        [Fact]
        public async Task ReconnectAsync_WhenInitializeRecovered_ShouldReturnTrue() {
            var adapter = new FakeLeadshaineEmcHardwareAdapter();
            var controller = CreateController(adapter);
            _ = await controller.InitializeAsync();
            adapter.InitializeCode = -1;

            var failed = await controller.ReconnectAsync();
            Assert.False(failed);
            Assert.Equal(EmcControllerStatus.Faulted, controller.Status);

            adapter.InitializeCode = 0;
            var recovered = await controller.ReconnectAsync();

            Assert.True(recovered);
            Assert.Equal(EmcControllerStatus.Connected, controller.Status);
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
                PollingIntervalMs = 50,
                InitializeRetryCount = 1,
                InitializeRetryDelayMs = 10,
                ReconnectBaseDelayMs = 10,
                ReconnectMaxDelayMs = 20
            };
            var pointBindings = new LeadshainePointBindingCollectionOptions {
                Points = []
            };

            return new LeadshaineEmcController(safeExecutor, connectionOptions, pointBindings, adapter);
        }
    }
}
