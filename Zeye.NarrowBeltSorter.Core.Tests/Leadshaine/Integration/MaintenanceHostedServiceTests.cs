using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Execution.Services.State;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Execution.Services.Hosted;
using System.Diagnostics;
using System.Collections.Concurrent;

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

            var changedStates = stateManager.GetChangedStatesSnapshot();
            Assert.Contains(SystemState.Maintenance, changedStates);
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

            var changedStates = new List<SystemState>(stateManager.GetChangedStatesSnapshot());
            Assert.Contains(SystemState.Paused, changedStates);
            Assert.Contains(SystemState.Maintenance, changedStates);
            var pausedIdx = changedStates.IndexOf(SystemState.Paused);
            var maintenanceIdx = changedStates.LastIndexOf(SystemState.Maintenance);
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

            var changedStates = stateManager.GetChangedStatesSnapshot();
            Assert.Contains(SystemState.Paused, changedStates);
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
            var changedStates = stateManager.GetChangedStatesSnapshot();
            Assert.DoesNotContain(SystemState.Maintenance, changedStates);
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

            var changedStates = stateManager.GetChangedStatesSnapshot();
            Assert.DoesNotContain(SystemState.Paused, changedStates);
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

            var changedStates = stateManager.GetChangedStatesSnapshot();
            Assert.DoesNotContain(SystemState.Maintenance, changedStates);
            await service.StopAsync(CancellationToken.None);
        }

        /// <summary>
        /// 检修状态只能切换到暂停或急停，其他状态切换应被驳回。
        /// </summary>
        [Fact]
        public async Task LocalSystemStateManager_MaintenanceOnlyAllowsPausedOrEmergencyStop() {
            var ioPanel = new FakeIoPanel();
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var manager = new LocalSystemStateManager(NullLogger<LocalSystemStateManager>.Instance, safeExecutor, ioPanel);
            await manager.ChangeStateAsync(SystemState.Maintenance);

            var toRunning = await manager.ChangeStateAsync(SystemState.Running);
            Assert.False(toRunning);
            Assert.Equal(SystemState.Maintenance, manager.CurrentState);

            var toPaused = await manager.ChangeStateAsync(SystemState.Paused);
            Assert.True(toPaused);
            Assert.Equal(SystemState.Paused, manager.CurrentState);
        }

        /// <summary>
        /// 运行态切换检修态时，应先切换暂停并等待后再切换检修态。
        /// </summary>
        [Fact]
        public async Task LocalSystemStateManager_RunningToMaintenance_ShouldPauseThenDelay() {
            var ioPanel = new FakeIoPanel();
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var manager = new LocalSystemStateManager(NullLogger<LocalSystemStateManager>.Instance, safeExecutor, ioPanel);
            var changedStates = new ConcurrentQueue<SystemState>();
            manager.StateChanged += (_, args) => changedStates.Enqueue(args.NewState);

            await manager.ChangeStateAsync(SystemState.Running);
            var stopwatch = Stopwatch.StartNew();
            var changed = await manager.ChangeStateAsync(SystemState.Maintenance);
            stopwatch.Stop();
            var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

            Assert.True(changed);
            Assert.Equal(SystemState.Maintenance, manager.CurrentState);
            Assert.True(elapsedMs >= 200, "运行到检修应包含约300ms停机过渡等待。");
            Assert.True(elapsedMs <= 1000, "运行到检修过渡等待不应异常过长。");
            var eventsObserved = await WaitForConditionAsync(
                () => {
                    var snapshot = changedStates.ToArray();
                    return snapshot.Contains(SystemState.Paused) && snapshot.Contains(SystemState.Maintenance);
                },
                timeoutMilliseconds: 1000,
                pollIntervalMilliseconds: 20);
            Assert.True(eventsObserved, "状态事件应在超时前包含 Paused 与 Maintenance。");
            var changedStateSnapshot = changedStates.ToArray().ToList();
            var pausedIdx = changedStateSnapshot.IndexOf(SystemState.Paused);
            var maintenanceIdx = changedStateSnapshot.LastIndexOf(SystemState.Maintenance);
            Assert.True(pausedIdx < maintenanceIdx, "Paused 应在 Maintenance 之前出现。");
        }

        /// <summary>
        /// 多急停按钮任一未释放时，状态不能切出急停。
        /// </summary>
        [Fact]
        public async Task LocalSystemStateManager_MultipleEmergencyStopsNotReleased_ShouldBlockExitEmergencyStop() {
            var ioPanel = new FakeIoPanel();
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var manager = new LocalSystemStateManager(NullLogger<LocalSystemStateManager>.Instance, safeExecutor, ioPanel);

            ioPanel.RaisePressed(IoPanelButtonType.EmergencyStop, "E1", "急停1");
            ioPanel.RaisePressed(IoPanelButtonType.EmergencyStop, "E2", "急停2");
            var enterEmergencyObserved = await WaitForConditionAsync(
                async () => await manager.ChangeStateAsync(SystemState.EmergencyStop),
                timeoutMilliseconds: 1000,
                pollIntervalMilliseconds: 20);
            var enterEmergency = enterEmergencyObserved;
            Assert.True(enterEmergency);

            ioPanel.RaiseReleased(IoPanelButtonType.EmergencyStop, "E1", "急停1");
            var toReadyBlockedObserved = await WaitForConditionAsync(
                async () => {
                    await manager.ChangeStateAsync(SystemState.EmergencyStop);
                    return !await manager.ChangeStateAsync(SystemState.Ready);
                },
                timeoutMilliseconds: 1000,
                pollIntervalMilliseconds: 20);
            var toReadyBlocked = toReadyBlockedObserved;
            Assert.True(toReadyBlocked);
            Assert.Equal(SystemState.EmergencyStop, manager.CurrentState);

            ioPanel.RaiseReleased(IoPanelButtonType.EmergencyStop, "E2", "急停2");
            var toReadyAllowedObserved = await WaitForConditionAsync(
                async () => await manager.ChangeStateAsync(SystemState.Ready),
                timeoutMilliseconds: 1000,
                pollIntervalMilliseconds: 20);
            var toReadyAllowed = toReadyAllowedObserved;
            Assert.True(toReadyAllowed);
            Assert.Equal(SystemState.Ready, manager.CurrentState);
        }

        /// <summary>
        /// 在指定超时内轮询等待条件满足。
        /// </summary>
        /// <param name="condition">目标条件。</param>
        /// <param name="timeoutMilliseconds">超时毫秒。</param>
        /// <param name="pollIntervalMilliseconds">轮询间隔毫秒。</param>
        /// <returns>是否在超时前满足条件。</returns>
        private static async Task<bool> WaitForConditionAsync(
            Func<bool> condition,
            int timeoutMilliseconds,
            int pollIntervalMilliseconds) {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds) {
                if (condition()) {
                    return true;
                }

                await Task.Delay(pollIntervalMilliseconds).ConfigureAwait(false);
            }

            return condition();
        }

        /// <summary>
        /// 在指定超时内轮询等待异步条件满足。
        /// </summary>
        /// <param name="condition">异步目标条件。</param>
        /// <param name="timeoutMilliseconds">超时毫秒。</param>
        /// <param name="pollIntervalMilliseconds">轮询间隔毫秒。</param>
        /// <returns>是否在超时前满足条件。</returns>
        private static async Task<bool> WaitForConditionAsync(
            Func<Task<bool>> condition,
            int timeoutMilliseconds,
            int pollIntervalMilliseconds) {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds) {
                if (await condition().ConfigureAwait(false)) {
                    return true;
                }

                await Task.Delay(pollIntervalMilliseconds).ConfigureAwait(false);
            }

            return await condition().ConfigureAwait(false);
        }
    }
}
