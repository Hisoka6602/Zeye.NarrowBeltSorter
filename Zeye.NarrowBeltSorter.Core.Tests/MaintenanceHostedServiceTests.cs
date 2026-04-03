using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.System;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Integration;
using Zeye.NarrowBeltSorter.Execution.Services.Hosted;

namespace Zeye.NarrowBeltSorter.Core.Tests {

    /// <summary>
    /// MaintenanceHostedService 状态流转与并发竞态测试。
    /// </summary>
    public sealed class MaintenanceHostedServiceTests {

        /// <summary>
        /// 检修开关打开时处于急停状态，应拒绝进入检修状态，系统状态保持 EmergencyStop。
        /// </summary>
        [Fact]
        public async Task SwitchOpen_WhenStateIsEmergencyStop_ShouldNotEnterMaintenance() {
            var (sensorManager, stateManager, service) = CreateServiceBundle(initialState: SystemState.EmergencyStop);
            var stateChanges = new List<SystemState>();
            stateManager.StateChanged += (_, args) => stateChanges.Add(args.NewState);

            using var cts = new CancellationTokenSource();
            await service.StartAsync(cts.Token);

            sensorManager.RaiseSensorStateChanged(IoPointType.MaintenanceSwitchSensor, IoState.High);

            // 等待足够时间让异步处理完成；急停状态不应发生任何新切换。
            await Task.Delay(500);

            await cts.CancelAsync();
            await service.StopAsync(CancellationToken.None);

            // 急停下触发检修开关不得引发状态切换（stateChanges 应为空）。
            Assert.Empty(stateChanges);
            Assert.Equal(SystemState.EmergencyStop, stateManager.CurrentState);
        }

        /// <summary>
        /// 检修开关打开时处于运行状态，应先切换为 Paused，延迟 300ms 后切换为 Maintenance。
        /// </summary>
        [Fact]
        public async Task SwitchOpen_WhenStateIsRunning_ShouldTransitionToPausedThenMaintenance() {
            var (sensorManager, stateManager, service) = CreateServiceBundle(initialState: SystemState.Running);

            using var cts = new CancellationTokenSource();
            await service.StartAsync(cts.Token);

            sensorManager.RaiseSensorStateChanged(IoPointType.MaintenanceSwitchSensor, IoState.High);

            // 等待超过 300ms 延迟让完整流程完成。
            var reached = await WaitForStateAsync(stateManager, SystemState.Maintenance, timeoutMs: 1000);

            await cts.CancelAsync();
            await service.StopAsync(CancellationToken.None);

            Assert.True(reached, "Running 状态下检修开关打开后，系统应切换为 Maintenance。");
        }

        /// <summary>
        /// 检修开关打开时处于暂停状态，应直接切换为 Maintenance。
        /// </summary>
        [Fact]
        public async Task SwitchOpen_WhenStateIsPaused_ShouldDirectlyEnterMaintenance() {
            var (sensorManager, stateManager, service) = CreateServiceBundle(initialState: SystemState.Paused);

            using var cts = new CancellationTokenSource();
            await service.StartAsync(cts.Token);

            sensorManager.RaiseSensorStateChanged(IoPointType.MaintenanceSwitchSensor, IoState.High);

            var reached = await WaitForStateAsync(stateManager, SystemState.Maintenance, timeoutMs: 500);

            await cts.CancelAsync();
            await service.StopAsync(CancellationToken.None);

            Assert.True(reached, "Paused 状态下检修开关打开后，系统应直接切换为 Maintenance。");
        }

        /// <summary>
        /// 检修开关关闭时，系统应切换为 Paused。
        /// </summary>
        [Fact]
        public async Task SwitchClose_WhenStateIsMaintenance_ShouldTransitionToPaused() {
            var (sensorManager, stateManager, service) = CreateServiceBundle(initialState: SystemState.Maintenance);

            using var cts = new CancellationTokenSource();
            await service.StartAsync(cts.Token);

            sensorManager.RaiseSensorStateChanged(IoPointType.MaintenanceSwitchSensor, IoState.Low);

            var reached = await WaitForStateAsync(stateManager, SystemState.Paused, timeoutMs: 500);

            await cts.CancelAsync();
            await service.StopAsync(CancellationToken.None);

            Assert.True(reached, "检修开关关闭后，系统应切换为 Paused。");
        }

        /// <summary>
        /// 检修开关打开期间，系统状态切换为 Running 时，应被驳回并强切回 Maintenance。
        /// </summary>
        [Fact]
        public async Task StateChangedToRunning_WhenSwitchIsOpen_ShouldRevertToMaintenance() {
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var stateManager = new FakeSystemStateManager(safeExecutor);
            await stateManager.ChangeStateAsync(SystemState.Paused);
            var sensorManager = new FakeSensorManager();
            var service = CreateService(safeExecutor, sensorManager, stateManager);

            using var cts = new CancellationTokenSource();
            await service.StartAsync(cts.Token);

            // 步骤1：先通过检修开关打开让服务记录 _maintenanceSwitchOpen=true。
            sensorManager.RaiseSensorStateChanged(IoPointType.MaintenanceSwitchSensor, IoState.High);
            var reachedMaintenance = await WaitForStateAsync(stateManager, SystemState.Maintenance, timeoutMs: 500);
            Assert.True(reachedMaintenance, "初始切换到 Maintenance 失败。");

            // 步骤2：外部强制切换为 Running，服务应立即驳回并切回 Maintenance。
            stateManager.RaiseStateChanged(SystemState.Running);
            var revertedToMaintenance = await WaitForStateAsync(stateManager, SystemState.Maintenance, timeoutMs: 500);

            await cts.CancelAsync();
            await service.StopAsync(CancellationToken.None);

            Assert.True(revertedToMaintenance, "检修开关打开期间切换至 Running 应被驳回并恢复 Maintenance。");
        }

        /// <summary>
        /// 等待系统状态达到预期值，超时返回 false。使用 Stopwatch 避免本地时间跳变。
        /// </summary>
        /// <param name="stateManager">系统状态管理器。</param>
        /// <param name="expected">期望的目标状态。</param>
        /// <param name="timeoutMs">超时时间（毫秒）。</param>
        /// <returns>是否在超时内达到期望状态。</returns>
        private static async Task<bool> WaitForStateAsync(
            FakeSystemStateManager stateManager,
            SystemState expected,
            int timeoutMs = 1000) {
            var sw = Stopwatch.StartNew();
            while (stateManager.CurrentState != expected && sw.ElapsedMilliseconds < timeoutMs) {
                await Task.Delay(20);
            }
            return stateManager.CurrentState == expected;
        }

        /// <summary>
        /// 创建包含传感器管理器、系统状态管理器和服务的测试套件。
        /// </summary>
        /// <param name="initialState">初始系统状态。</param>
        /// <returns>（传感器管理器、状态管理器、服务）元组。</returns>
        private static (FakeSensorManager SensorManager, FakeSystemStateManager StateManager, MaintenanceHostedService Service) CreateServiceBundle(
            SystemState initialState = SystemState.Ready) {
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var stateManager = new FakeSystemStateManager(safeExecutor);
            stateManager.RaiseStateChanged(initialState);
            var sensorManager = new FakeSensorManager();
            var service = CreateService(safeExecutor, sensorManager, stateManager);
            return (sensorManager, stateManager, service);
        }

        /// <summary>
        /// 创建检修服务实例（无信号塔注入）。
        /// </summary>
        /// <param name="safeExecutor">安全执行器。</param>
        /// <param name="sensorManager">传感器管理器。</param>
        /// <param name="systemStateManager">系统状态管理器。</param>
        /// <returns>服务实例。</returns>
        private static MaintenanceHostedService CreateService(
            SafeExecutor safeExecutor,
            FakeSensorManager sensorManager,
            FakeSystemStateManager systemStateManager) {
            return new MaintenanceHostedService(
                NullLogger<MaintenanceHostedService>.Instance,
                safeExecutor,
                sensorManager,
                systemStateManager,
                OptionsMonitorTestHelper.Create(new LoopTrackServiceOptions()),
                new EmptyServiceProvider());
        }

        /// <summary>
        /// 空服务提供者：用于不依赖信号塔的测试场景。
        /// </summary>
        private sealed class EmptyServiceProvider : IServiceProvider {
            /// <summary>
            /// 始终返回 null，表示未注册任何服务。
            /// </summary>
            /// <param name="serviceType">服务类型。</param>
            /// <returns>null。</returns>
            public object? GetService(Type serviceType) => null;
        }
    }
}
