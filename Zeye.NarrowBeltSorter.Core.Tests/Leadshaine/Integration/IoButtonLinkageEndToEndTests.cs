using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Execution.Services.Hosted;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Integration {
    /// <summary>
    /// IO 按钮 -> 系统状态 -> IO 联动端到端测试。
    /// </summary>
    public sealed class IoButtonLinkageEndToEndTests {
        /// <summary>
        /// 启动按钮按下后，应触发 Running 状态的联动 IO 写出。
        /// </summary>
        [Fact]
        public async Task StartButtonPressed_ShouldTriggerRunningLinkageWrite() {
            // 步骤1：构建测试桩与两个托管服务
            var ioPanel = new FakeIoPanel();
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var stateManager = new FakeSystemStateManager(safeExecutor);
            var emc = new FakeLeadshaineEmcController();
            var transitionService = new IoPanelStateTransitionHostedService(
                NullLogger<IoPanelStateTransitionHostedService>.Instance,
                safeExecutor,
                ioPanel,
                stateManager);
            var linkageService = new IoLinkageHostedService(
                NullLogger<IoLinkageHostedService>.Instance,
                stateManager,
                emc,
                Microsoft.Extensions.Options.Options.Create(new LeadshaineIoLinkageOptions {
                    Enabled = true,
                    Points = [
                        new LeadshaineIoLinkagePointOptions {
                            RelatedSystemState = SystemState.Running,
                            PointId = "Q-Run",
                            TriggerValue = true,
                            DelayMs = 0,
                            DurationMs = 0
                        }
                    ]
                }));

            // 步骤2：启动联动服务与状态桥接服务
            await linkageService.StartAsync(CancellationToken.None);
            Assert.True(stateManager.WaitForSubscriber(200));
            await transitionService.StartAsync(CancellationToken.None);

            // 步骤3：模拟启动按钮按下，断言联动 IO 已写出
            ioPanel.RaisePressed(IoPanelButtonType.Start);
            Assert.True(emc.WaitForWriteCount(1, 1000));

            // 步骤4：停止所有服务并验证写出记录
            await transitionService.StopAsync(CancellationToken.None);
            await linkageService.StopAsync(CancellationToken.None);
            Assert.Contains(emc.WriteIoCalls, x => x.PointId == "Q-Run" && x.Value);
        }
    }
}
