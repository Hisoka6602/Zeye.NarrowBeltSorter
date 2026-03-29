using Zeye.NarrowBeltSorter.Core.Enums.Emc;

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
            var testContext = LeadshaineEmcControllerTestFactory.CreateWithAdapter();
            var controller = testContext.Controller;
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
            var testContext = LeadshaineEmcControllerTestFactory.CreateWithAdapter();
            testContext.Adapter.InitializeCode = -1;
            var controller = testContext.Controller;

            var result = await controller.InitializeAsync();

            Assert.False(result);
            Assert.Equal(EmcControllerStatus.Faulted, controller.Status);
            await controller.DisposeAsync();
        }

    }
}
