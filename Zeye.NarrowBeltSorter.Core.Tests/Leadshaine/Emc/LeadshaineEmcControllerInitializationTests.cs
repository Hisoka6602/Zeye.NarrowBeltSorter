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
            var testContext = LeadshaineEmcControllerTestFactory.CreateWithAdapter(includeOutputPoint: true, initializeRetryCount: 1);
            var controller = testContext.Controller;
            var initializedTriggered = false;
            controller.Initialized += (_, _) => initializedTriggered = true;

            var result = await controller.InitializeAsync();

            Assert.True(result);
            Assert.Equal(EmcControllerStatus.Connected, controller.Status);
            Assert.True(initializedTriggered);
            Assert.NotNull(testContext.Adapter.LastInitializeArgs);
            Assert.Equal((ushort)0, testContext.Adapter.LastInitializeArgs.Value.CardNo);
            Assert.Null(testContext.Adapter.LastInitializeArgs.Value.ControllerIp);
            await controller.DisposeAsync();
        }

        /// <summary>
        /// 初始化失败后应进入故障状态。
        /// </summary>
        [Fact]
        public async Task InitializeAsync_WhenBoardInitFailed_ShouldSetFaulted() {
            var testContext = LeadshaineEmcControllerTestFactory.CreateWithAdapter(includeOutputPoint: true, initializeRetryDelayMs: 10);
            testContext.Adapter.InitializeCode = -1;
            var controller = testContext.Controller;

            var result = await controller.InitializeAsync();

            Assert.False(result);
            Assert.Equal(EmcControllerStatus.Faulted, controller.Status);
            await controller.DisposeAsync();
        }

    }
}
