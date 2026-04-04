using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Execution.Services.Hosted;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Integration {
    /// <summary>
    /// IoPanelStateTransitionHostedService 行为测试。
    /// </summary>
    public sealed class IoPanelStateTransitionHostedServiceTests {
        /// <summary>
        /// 按下不同按钮后，系统状态应切换到对应目标状态。
        /// </summary>
        [Theory]
        [InlineData(IoPanelButtonType.Start, SystemState.Running)]
        [InlineData(IoPanelButtonType.Stop, SystemState.Paused)]
        [InlineData(IoPanelButtonType.EmergencyStop, SystemState.EmergencyStop)]
        [InlineData(IoPanelButtonType.Reset, SystemState.Booting)]
        public async Task ButtonPressed_ShouldChangeState(IoPanelButtonType buttonType, SystemState expectedState) {
            var ioPanel = new FakeIoPanel();
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var stateManager = new TrackingSystemStateManager(safeExecutor);
            var service = CreateService(ioPanel, stateManager, safeExecutor);

            await service.StartAsync(CancellationToken.None);
            ioPanel.RaisePressed(buttonType);
            // Start 事件链路包含 StartupWarning 过渡，需留足时间覆盖 StartupWarningDurationMs 与调度抖动。
            var assertDelayMs = buttonType == IoPanelButtonType.Start ? 220 : 80;
            await Task.Delay(assertDelayMs);

            var changedStates = stateManager.GetChangedStatesSnapshot();
            Assert.Contains(expectedState, changedStates);
            await service.StopAsync(CancellationToken.None);
        }

        /// <summary>
        /// 急停按钮释放后，系统状态应切换到 Ready。
        /// </summary>
        [Fact]
        public async Task EmergencyReleased_ShouldChangeStateToReady() {
            var ioPanel = new FakeIoPanel();
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var stateManager = new TrackingSystemStateManager(safeExecutor);
            var service = CreateService(ioPanel, stateManager, safeExecutor);

            await service.StartAsync(CancellationToken.None);
            ioPanel.RaiseReleased(IoPanelButtonType.EmergencyStop);
            await Task.Delay(80);

            var changedStates = stateManager.GetChangedStatesSnapshot();
            Assert.Contains(SystemState.Ready, changedStates);
            await service.StopAsync(CancellationToken.None);
        }

        /// <summary>
        /// 创建被测服务实例。
        /// </summary>
        /// <param name="ioPanel">IoPanel 测试桩。</param>
        /// <param name="stateManager">系统状态管理器测试桩。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <returns>IoPanelStateTransitionHostedService 实例。</returns>
        private static IoPanelStateTransitionHostedService CreateService(
            FakeIoPanel ioPanel,
            TrackingSystemStateManager stateManager,
            SafeExecutor safeExecutor) {
            return new IoPanelStateTransitionHostedService(
                NullLogger<IoPanelStateTransitionHostedService>.Instance,
                safeExecutor,
                ioPanel,
                stateManager,
                OptionsMonitorTestHelper.Create(
                    new LeadshaineIoPanelStateTransitionOptions {
                        StartupWarningDurationMs = 80
                    }));
        }
    }
}
