using Zeye.NarrowBeltSorter.Core.Enums.Emc;

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
            var testContext = LeadshaineEmcControllerTestFactory.CreateWithAdapter();
            var controller = testContext.Controller;
            _ = await controller.InitializeAsync();
            testContext.Adapter.InitializeCode = -1;

            var failed = await controller.ReconnectAsync();
            Assert.False(failed);
            Assert.Equal(EmcControllerStatus.Faulted, controller.Status);

            testContext.Adapter.InitializeCode = 0;
            var recovered = await controller.ReconnectAsync();

            Assert.True(recovered);
            Assert.Equal(EmcControllerStatus.Connected, controller.Status);
            await controller.DisposeAsync();
        }
    }
}
