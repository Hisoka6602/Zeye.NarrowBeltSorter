using Zeye.NarrowBeltSorter.Core.Enums.Emc;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Emc {
    /// <summary>
    /// Leadshaine EMC 控制器监控循环测试（断链检测、点位快照、TryGetMonitoredPoint）。
    /// </summary>
    public sealed class LeadshaineEmcControllerMonitoringTests {
        /// <summary>
        /// 注册点位后轮询写入，TryGetMonitoredPoint 应返回最新快照值。
        /// </summary>
        [Fact]
        public async Task MonitoringLoop_AfterSetMonitoredPoints_TryGetMonitoredPoint_ShouldReturnLatestValue() {
            var testContext = LeadshaineEmcControllerTestFactory.CreateWithAdapter(includeOutputPoint: false);
            // 步骤1：设置端口0的输入位图，令 BitIndex=1 为高电平。
            testContext.Adapter.InPortValues[(0, 0)] = 0b_0000_0010u;

            // 步骤2：初始化并注册监控点位。
            _ = await testContext.Controller.InitializeAsync();
            _ = await testContext.Controller.SetMonitoredIoPointsAsync(["I-01"]);

            // 步骤3：等待至少一个轮询周期（PollingIntervalMs=50ms）。
            await Task.Delay(150);

            // 步骤4：TryGetMonitoredPoint 应返回最新读取到的值。
            var found = testContext.Controller.TryGetMonitoredPoint("I-01", out var info);
            Assert.True(found);
            Assert.Equal("I-01", info.PointId);
            Assert.True(info.Value);

            await testContext.Controller.DisposeAsync();
        }

        /// <summary>
        /// 未注册的点位 TryGetMonitoredPoint 应返回 false。
        /// </summary>
        [Fact]
        public async Task TryGetMonitoredPoint_WhenNotRegistered_ShouldReturnFalse() {
            var testContext = LeadshaineEmcControllerTestFactory.CreateWithAdapter(includeOutputPoint: false);

            _ = await testContext.Controller.InitializeAsync();

            var found = testContext.Controller.TryGetMonitoredPoint("I-01", out _);
            Assert.False(found);

            await testContext.Controller.DisposeAsync();
        }

        /// <summary>
        /// 监控循环检测到断链返回码时，应切换为 Disconnected 状态并退出监控循环。
        /// </summary>
        [Fact]
        public async Task MonitoringLoop_WhenDisconnectedReadCode_ShouldSetDisconnectedAndExit() {
            var testContext = LeadshaineEmcControllerTestFactory.CreateWithAdapter(
                includeOutputPoint: false,
                reconnectBaseDelayMs: 5000,
                reconnectMaxDelayMs: 10000);

            var statusChangedArgs = new List<EmcControllerStatus>();
            testContext.Controller.StatusChanged += (_, e) => {
                lock (statusChangedArgs) {
                    statusChangedArgs.Add(e.NewStatus);
                }
            };

            // 步骤1：初始化并注册监控点位。
            _ = await testContext.Controller.InitializeAsync();
            _ = await testContext.Controller.SetMonitoredIoPointsAsync(["I-01"]);

            // 步骤2：设置断链返回码 9，并使重连失败（避免重连任务立即改回 Connected）。
            testContext.Adapter.InPortValues[(0, 0)] = 9u;
            testContext.Adapter.InitializeCode = -1;

            // 步骤3：等待监控循环检测到断链（PollingIntervalMs=50ms）。
            await Task.Delay(200);

            Assert.Contains(EmcControllerStatus.Disconnected, statusChangedArgs);

            // 步骤4：断链后快照应已清空。
            var found = testContext.Controller.TryGetMonitoredPoint("I-01", out _);
            Assert.False(found);

            await testContext.Controller.DisposeAsync();
        }

        /// <summary>
        /// SetMonitoredIoPointsAsync 应对重复点位只注册一次，快照中不出现重复条目。
        /// </summary>
        [Fact]
        public async Task SetMonitoredIoPointsAsync_DuplicatePointId_ShouldRegisterOnce() {
            var testContext = LeadshaineEmcControllerTestFactory.CreateWithAdapter(includeOutputPoint: false);
            testContext.Adapter.InPortValues[(0, 0)] = 0b_0000_0010u;

            _ = await testContext.Controller.InitializeAsync();
            _ = await testContext.Controller.SetMonitoredIoPointsAsync(["I-01"]);
            var result = await testContext.Controller.SetMonitoredIoPointsAsync(["I-01"]);

            // 步骤：等待至少一个轮询周期，确保快照已更新。
            await Task.Delay(150);

            Assert.True(result);
            // 点位只应在快照中出现一次（Dictionary 保证唯一键）。
            var points = testContext.Controller.MonitoredIoPoints;
            Assert.Single(points);
            Assert.Equal("I-01", points.First().PointId);

            await testContext.Controller.DisposeAsync();
        }
    }
}
