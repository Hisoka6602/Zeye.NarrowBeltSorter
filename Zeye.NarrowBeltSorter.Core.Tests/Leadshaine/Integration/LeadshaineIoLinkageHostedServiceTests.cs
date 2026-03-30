using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Execution.Services.Hosted;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Integration {
    /// <summary>
    /// Leadshaine IoLinkageHostedService 联动测试。
    /// </summary>
    public sealed class LeadshaineIoLinkageHostedServiceTests {
        private const int TriggerAndReverseWaitMs = 2000;
        private const int NoMatchWaitMs = 120;

        /// <summary>
        /// 状态匹配规则时应执行触发与回写。
        /// </summary>
        [Fact]
        public async Task StartAsync_WhenStateMatched_ShouldWriteTriggerAndReverse() {
            var fakeEmc = new FakeLeadshaineEmcController();
            var executor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var fakeStateManager = new FakeSystemStateManager(executor);
            var service = CreateService(
                fakeStateManager,
                fakeEmc,
                new LeadshaineIoLinkageOptions {
                    Enabled = true,
                    Points = [
                        new LeadshaineIoLinkagePointOptions {
                            RelatedSystemState = SystemState.Running,
                            PointId = "Q-01",
                            TriggerValue = true,
                            DelayMs = 0,
                            DurationMs = 50
                        }
                    ]
                });

            await service.StartAsync(CancellationToken.None);
            Assert.True(fakeStateManager.WaitForSubscriber(NoMatchWaitMs));
            fakeStateManager.RaiseStateChanged(SystemState.Running);
            Assert.True(fakeEmc.WaitForWriteCount(2, TriggerAndReverseWaitMs));
            await service.StopAsync(CancellationToken.None);

            Assert.Contains(fakeEmc.WriteIoCalls, call => call.PointId == "Q-01" && call.Value);
            Assert.Contains(fakeEmc.WriteIoCalls, call => call.PointId == "Q-01" && call.Value == false);
        }

        /// <summary>
        /// 状态不匹配规则时不应写入 IO。
        /// </summary>
        [Fact]
        public async Task StartAsync_WhenStateNotMatched_ShouldNotWriteIo() {
            var fakeEmc = new FakeLeadshaineEmcController();
            var executor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var fakeStateManager = new FakeSystemStateManager(executor);
            var service = CreateService(
                fakeStateManager,
                fakeEmc,
                new LeadshaineIoLinkageOptions {
                    Enabled = true,
                    Points = [
                        new LeadshaineIoLinkagePointOptions {
                            RelatedSystemState = SystemState.Running,
                            PointId = "Q-01",
                            TriggerValue = true,
                            DelayMs = 0,
                            DurationMs = 0
                        }
                    ]
                });

            await service.StartAsync(CancellationToken.None);
            Assert.True(fakeStateManager.WaitForSubscriber(NoMatchWaitMs));
            fakeStateManager.RaiseStateChanged(SystemState.Paused);
            Assert.False(fakeEmc.WaitForWriteCount(1, NoMatchWaitMs));
            await service.StopAsync(CancellationToken.None);

            Assert.Empty(fakeEmc.WriteIoCalls);
        }

        /// <summary>
        /// 高频状态变更时应仅消费最后一个状态，避免回放过期状态。
        /// </summary>
        [Fact]
        public async Task StartAsync_WhenStateChangesBurst_ShouldProcessLatestStateOnly() {
            // 步骤1：构造双状态规则，分别映射 Running 与 Paused，便于验证“仅保留最新状态”语义。
            var fakeEmc = new FakeLeadshaineEmcController();
            var executor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var fakeStateManager = new FakeSystemStateManager(executor);
            var service = CreateService(
                fakeStateManager,
                fakeEmc,
                new LeadshaineIoLinkageOptions {
                    Enabled = true,
                    Points = [
                        new LeadshaineIoLinkagePointOptions {
                            RelatedSystemState = SystemState.Running,
                            PointId = "Q-01",
                            TriggerValue = true,
                            DelayMs = 30,
                            DurationMs = 0
                        },
                        new LeadshaineIoLinkagePointOptions {
                            RelatedSystemState = SystemState.Paused,
                            PointId = "Q-02",
                            TriggerValue = true,
                            DelayMs = 30,
                            DurationMs = 0
                        }
                    ]
                });

            // 步骤2：连续投递两个状态事件，后者应覆盖前者成为最终处理状态。
            await service.StartAsync(CancellationToken.None);
            Assert.True(fakeStateManager.WaitForSubscriber(NoMatchWaitMs));
            fakeStateManager.RaiseStateChanged(SystemState.Running);
            fakeStateManager.RaiseStateChanged(SystemState.Paused);
            Assert.True(fakeEmc.WaitForWriteCount(1, TriggerAndReverseWaitMs));
            await service.StopAsync(CancellationToken.None);

            // 步骤3：断言仅执行最新状态对应的联动规则，避免过期状态回放。
            Assert.DoesNotContain(fakeEmc.WriteIoCalls, call => call.PointId == "Q-01");
            Assert.Contains(fakeEmc.WriteIoCalls, call => call.PointId == "Q-02" && call.Value);
        }

        /// <summary>
        /// 创建 IoLinkageHostedService 测试实例。
        /// </summary>
        /// <param name="stateManager">系统状态管理器测试桩。</param>
        /// <param name="emcController">EMC 控制器测试桩。</param>
        /// <param name="options">联动配置。</param>
        /// <returns>托管服务实例。</returns>
        private static IoLinkageHostedService CreateService(
            FakeSystemStateManager stateManager,
            FakeLeadshaineEmcController emcController,
            LeadshaineIoLinkageOptions options) {
            return new IoLinkageHostedService(
                NullLogger<IoLinkageHostedService>.Instance,
                stateManager,
                emcController,
                global::Microsoft.Extensions.Options.Options.Create(options));
        }
    }
}
