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
            var testContext = LeadshaineEmcControllerTestFactory.CreateWithAdapter(includeOutputPoint: true, reconnectMaxDelayMs: 20);
            var controller = testContext.Controller;

            var result = await controller.WriteIoAsync("Q-01", true);

            Assert.True(result);
            Assert.NotNull(testContext.Adapter.LastWriteOutBit);
            Assert.Equal((ushort)0, testContext.Adapter.LastWriteOutBit.Value.CardNo);
            Assert.Equal((ushort)3, testContext.Adapter.LastWriteOutBit.Value.BitNo);
            Assert.Equal((ushort)1, testContext.Adapter.LastWriteOutBit.Value.OnOff);
            await controller.DisposeAsync();
        }

        /// <summary>
        /// 写输入点应被拒绝。
        /// </summary>
        [Fact]
        public async Task WriteIoAsync_WhenInputPoint_ShouldReturnFalse() {
            var testContext = LeadshaineEmcControllerTestFactory.CreateWithAdapter(includeOutputPoint: false);
            var controller = testContext.Controller;

            var result = await controller.WriteIoAsync("I-01", true);

            Assert.False(result);
            Assert.Null(testContext.Adapter.LastWriteOutBit);
            await controller.DisposeAsync();
        }

    }
}
