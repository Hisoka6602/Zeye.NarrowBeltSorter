using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Execution.Services.Hosted;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Integration {
    /// <summary>
    /// Leadshaine IoLinkageHostedService 联动测试。
    /// </summary>
    public sealed class LeadshaineIoLinkageHostedServiceTests {
        private const int TriggerAndReverseWaitMs = 180;
        private const int NoMatchWaitMs = 120;

        /// <summary>
        /// 状态匹配规则时应执行触发与回写。
        /// </summary>
        [Fact]
        public async Task StartAsync_WhenStateMatched_ShouldWriteTriggerAndReverse() {
            var fakeEmc = new FakeLeadshaineEmcController();
            var fakeStateManager = new FakeSystemStateManager();
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
            fakeStateManager.RaiseStateChanged(SystemState.Running);
            // 等待触发写入 + 50ms 回写窗口 + 调度余量，确保断言稳定。
            await Task.Delay(TriggerAndReverseWaitMs);
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
            var fakeStateManager = new FakeSystemStateManager();
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
            fakeStateManager.RaiseStateChanged(SystemState.Paused);
            // 等待一个最小处理窗口，验证未命中规则时不会产生写入记录。
            await Task.Delay(NoMatchWaitMs);
            await service.StopAsync(CancellationToken.None);

            Assert.Empty(fakeEmc.WriteIoCalls);
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
