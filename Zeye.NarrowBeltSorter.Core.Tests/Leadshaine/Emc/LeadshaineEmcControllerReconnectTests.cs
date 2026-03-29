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
            var testContext = LeadshaineEmcControllerTestFactory.CreateWithAdapter(includeOutputPoint: true, reconnectBaseDelayMs: 10);
            _ = await testContext.Controller.InitializeAsync();
            testContext.Adapter.InitializeCode = -1;

            var failed = await testContext.Controller.ReconnectAsync();
            Assert.False(failed);
            Assert.Equal(EmcControllerStatus.Faulted, testContext.Controller.Status);

            testContext.Adapter.InitializeCode = 0;
            var recovered = await testContext.Controller.ReconnectAsync();

            Assert.True(recovered);
            Assert.Equal(EmcControllerStatus.Connected, testContext.Controller.Status);
            await testContext.Controller.DisposeAsync();
        }
    }
}
