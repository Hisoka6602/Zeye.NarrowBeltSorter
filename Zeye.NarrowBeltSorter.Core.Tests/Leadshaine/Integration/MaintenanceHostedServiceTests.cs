using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Execution.Services.Hosted;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Integration {
    /// <summary>
    /// MaintenanceHostedService 行为测试。
    /// </summary>
    public sealed class MaintenanceHostedServiceTests {

        /// <summary>
        /// 工具方法：创建被测服务实例。
        /// </summary>
        /// <param name="ioPanel">IoPanel 测试桩。</param>
        /// <param name="stateManager">系统状态管理器测试桩。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <returns>MaintenanceHostedService 实例。</returns>
        private static MaintenanceHostedService CreateService(
            FakeIoPanel ioPanel,
            TrackingSystemStateManager stateManager,
            SafeExecutor safeExecutor) {
            return new MaintenanceHostedService(
                NullLogger<MaintenanceHostedService>.Instance,
                safeExecutor,
                ioPanel,
                stateManager);
        }

        /// <summary>
        /// 非运行态下检修开关打开，应直接切换至 Maintenance 状态。
        /// </summary>
        [Fact]
        public async Task SwitchOpened_FromPaused_ShouldTransitionToMaintenance() {
            var ioPanel = new FakeIoPanel();
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var stateManager = new TrackingSystemStateManager(safeExecutor);
            await stateManager.ChangeStateAsync(SystemState.Paused);
            var service = CreateService(ioPanel, stateManager, safeExecutor);

            await service.StartAsync(CancellationToken.None);
            ioPanel.RaisePressed(IoPanelButtonType.MaintenanceSwitch);
            await Task.Delay(100);

            Assert.Contains(SystemState.Maintenance, stateManager.ChangedStates);
            await service.StopAsync(CancellationToken.None);
        }

        /// <summary>
        /// 运行态下检修开关打开，应先切换至 Paused，等待过渡后切换至 Maintenance。
        /// </summary>
        [Fact]
        public async Task SwitchOpened_FromRunning_ShouldTransitionToPausedThenMaintenance() {
            var ioPanel = new FakeIoPanel();
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var stateManager = new TrackingSystemStateManager(safeExecutor);
            await stateManager.ChangeStateAsync(SystemState.Running);
            var service = CreateService(ioPanel, stateManager, safeExecutor);

            await service.StartAsync(CancellationToken.None);
            ioPanel.RaisePressed(IoPanelButtonType.MaintenanceSwitch);
            // 等待超过 300ms 过渡延迟。
            await Task.Delay(500);

            Assert.Contains(SystemState.Paused, stateManager.ChangedStates);
            Assert.Contains(SystemState.Maintenance, stateManager.ChangedStates);
            var pausedIdx = stateManager.ChangedStates.IndexOf(SystemState.Paused);
            var maintenanceIdx = stateManager.ChangedStates.LastIndexOf(SystemState.Maintenance);
            Assert.True(pausedIdx < maintenanceIdx, "Paused 应在 Maintenance 之前出现。");
            await service.StopAsync(CancellationToken.None);
        }

        /// <summary>
        /// 检修开关打开期间，任何切换至 Running 的尝试都应被强制回到 Maintenance。
        /// </summary>
        [Fact]
        public async Task SwitchOpen_ThenRunningState_ShouldForceBackToMaintenance() {
            var ioPanel = new FakeIoPanel();
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var stateManager = new TrackingSystemStateManager(safeExecutor);
            var service = CreateService(ioPanel, stateManager, safeExecutor);

            await service.StartAsync(CancellationToken.None);
            ioPanel.RaisePressed(IoPanelButtonType.MaintenanceSwitch);
            await Task.Delay(100);

            // 人为切换至 Running，模拟外部操作。
            await stateManager.ChangeStateAsync(SystemState.Running);
            await Task.Delay(100);

            // 最后一个状态应回到 Maintenance。
            Assert.Equal(SystemState.Maintenance, stateManager.CurrentState);
            await service.StopAsync(CancellationToken.None);
        }

        /// <summary>
        /// 检修开关关闭时，应切换至 Paused（急停状态除外）。
        /// </summary>
        [Fact]
        public async Task SwitchClosed_ShouldTransitionToPaused() {
            var ioPanel = new FakeIoPanel();
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var stateManager = new TrackingSystemStateManager(safeExecutor);
            await stateManager.ChangeStateAsync(SystemState.Maintenance);
            var service = CreateService(ioPanel, stateManager, safeExecutor);

            await service.StartAsync(CancellationToken.None);
            ioPanel.RaiseReleased(IoPanelButtonType.MaintenanceSwitch);
            await Task.Delay(100);

            Assert.Contains(SystemState.Paused, stateManager.ChangedStates);
            await service.StopAsync(CancellationToken.None);
        }

        /// <summary>
        /// 急停状态下检修开关打开，不应切换系统状态（仅蜂鸣，此处无信号塔故直接返回）。
        /// </summary>
        [Fact]
        public async Task SwitchOpened_DuringEmergencyStop_ShouldNotChangeState() {
            var ioPanel = new FakeIoPanel();
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var stateManager = new TrackingSystemStateManager(safeExecutor);
            await stateManager.ChangeStateAsync(SystemState.EmergencyStop);
            var service = CreateService(ioPanel, stateManager, safeExecutor);

            await service.StartAsync(CancellationToken.None);
            stateManager.ClearHistory();
            ioPanel.RaisePressed(IoPanelButtonType.MaintenanceSwitch);
            await Task.Delay(100);

            // 急停状态下不应切换系统状态。
            Assert.DoesNotContain(SystemState.Maintenance, stateManager.ChangedStates);
            await service.StopAsync(CancellationToken.None);
        }

        /// <summary>
        /// 急停状态下检修开关关闭，不应切换系统状态。
        /// </summary>
        [Fact]
        public async Task SwitchClosed_DuringEmergencyStop_ShouldNotChangeState() {
            var ioPanel = new FakeIoPanel();
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var stateManager = new TrackingSystemStateManager(safeExecutor);
            await stateManager.ChangeStateAsync(SystemState.EmergencyStop);
            var service = CreateService(ioPanel, stateManager, safeExecutor);

            await service.StartAsync(CancellationToken.None);
            stateManager.ClearHistory();
            ioPanel.RaiseReleased(IoPanelButtonType.MaintenanceSwitch);
            await Task.Delay(100);

            Assert.DoesNotContain(SystemState.Paused, stateManager.ChangedStates);
            await service.StopAsync(CancellationToken.None);
        }

        /// <summary>
        /// 快速 opened+closed（在 300ms 过渡延迟内关闭），不应切换至 Maintenance。
        /// </summary>
        [Fact]
        public async Task SwitchOpenedThenQuicklyClosed_ShouldNotTransitionToMaintenance() {
            var ioPanel = new FakeIoPanel();
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var stateManager = new TrackingSystemStateManager(safeExecutor);
            await stateManager.ChangeStateAsync(SystemState.Running);
            var service = CreateService(ioPanel, stateManager, safeExecutor);

            await service.StartAsync(CancellationToken.None);
            ioPanel.RaisePressed(IoPanelButtonType.MaintenanceSwitch);
            await Task.Delay(50);
            // 在 300ms 过渡延迟结束前关闭。
            ioPanel.RaiseReleased(IoPanelButtonType.MaintenanceSwitch);
            await Task.Delay(400);

            Assert.DoesNotContain(SystemState.Maintenance, stateManager.ChangedStates);
            await service.StopAsync(CancellationToken.None);
        }
    }
}
