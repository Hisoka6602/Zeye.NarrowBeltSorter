using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using Zeye.NarrowBeltSorter.Core.Enums.Chutes;
using Zeye.NarrowBeltSorter.Core.Enums.Device;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Events.Chutes;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Utilities.Chutes;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Infrared;
using Zeye.NarrowBeltSorter.Drivers.Vendors.ZhiQian;

namespace Zeye.NarrowBeltSorter.Core.Tests {

    /// <summary>
    /// 智嵌格口管理器行为测试（映射合法性、连接流转、状态变更、异常隔离）。
    /// </summary>
    public sealed class ZhiQianChuteManagerTests {

        /// <summary>
        /// 配置合法性：空映射应拒绝启动。
        /// </summary>
        [Fact]
        public void Constructor_EmptyChuteToDoMap_ShouldThrow() {
            var options = BuildOptions(new Dictionary<long, int>());
            var adapter = new FakeZhiQianClientAdapter();
            Assert.Throws<ArgumentException>(() => CreateManager(options, adapter));
        }

        /// <summary>
        /// 配置合法性：Y 路越界应拒绝启动。
        /// </summary>
        [Fact]
        public void Constructor_DoIndexOutOfRange_ShouldThrow() {
            var options = BuildOptions(new Dictionary<long, int> { { 101L, 33 } });
            var adapter = new FakeZhiQianClientAdapter();
            Assert.Throws<ArgumentException>(() => CreateManager(options, adapter));
        }

        /// <summary>
        /// 配置合法性：重复 Y 路应拒绝启动。
        /// </summary>
        [Fact]
        public void Constructor_DuplicateDoIndex_ShouldThrow() {
            var options = BuildOptions(new Dictionary<long, int> { { 101L, 1 }, { 102L, 1 } });
            var adapter = new FakeZhiQianClientAdapter();
            Assert.Throws<ArgumentException>(() => CreateManager(options, adapter));
        }

        /// <summary>
        /// 连接成功后 ConnectionStatus 应变为 Connected，并触发 ConnectionStatusChanged 事件。
        /// </summary>
        [Fact]
        public async Task ConnectAsync_Success_ShouldSetConnectedStatus() {
            var adapter = new FakeZhiQianClientAdapter();
            var manager = CreateManager(BuildValidOptions(), adapter);
            var statusChanges = new List<(DeviceConnectionStatus Old, DeviceConnectionStatus New)>();
            manager.ConnectionStatusChanged += (_, args) => statusChanges.Add((args.OldStatus, args.NewStatus));

            var result = await manager.ConnectAsync();

            Assert.True(result);
            Assert.Equal(DeviceConnectionStatus.Connected, manager.ConnectionStatus);
            Assert.Equal(1, adapter.ConnectCount);
            Assert.Contains(statusChanges, x => x.Old == DeviceConnectionStatus.Disconnected && x.New == DeviceConnectionStatus.Connecting);
            Assert.Contains(statusChanges, x => x.Old == DeviceConnectionStatus.Connecting && x.New == DeviceConnectionStatus.Connected);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 连接失败时 ConnectionStatus 应变为 Faulted，并触发 Faulted 事件。
        /// </summary>
        [Fact]
        public async Task ConnectAsync_Failure_ShouldSetFaultedStatusAndRaiseFaultedEvent() {
            var adapter = new FakeZhiQianClientAdapter { ThrowOnConnect = true };
            var manager = CreateManager(BuildValidOptions(), adapter);
            var faultedArgs = new List<ChuteManagerFaultedEventArgs>();
            manager.Faulted += (_, args) => faultedArgs.Add(args);

            var result = await manager.ConnectAsync();

            Assert.False(result);
            Assert.Equal(DeviceConnectionStatus.Faulted, manager.ConnectionStatus);
            Assert.NotEmpty(faultedArgs);
            Assert.Equal("ConnectAsync", faultedArgs[0].Operation);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 断开后 ConnectionStatus 应变为 Disconnected，并触发 ConnectionStatusChanged 事件。
        /// </summary>
        [Fact]
        public async Task DisconnectAsync_AfterConnect_ShouldSetDisconnectedStatus() {
            var adapter = new FakeZhiQianClientAdapter();
            var manager = CreateManager(BuildValidOptions(), adapter);
            await manager.ConnectAsync();
            var statusChanges = new List<(DeviceConnectionStatus Old, DeviceConnectionStatus New)>();
            manager.ConnectionStatusChanged += (_, args) => statusChanges.Add((args.OldStatus, args.NewStatus));

            var result = await manager.DisconnectAsync();

            Assert.True(result);
            Assert.Equal(DeviceConnectionStatus.Disconnected, manager.ConnectionStatus);
            Assert.Contains(statusChanges, x => x.New == DeviceConnectionStatus.Disconnected);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// SetForcedChuteAsync 指定合法格口时应写入对应 Y 路，并触发 ForcedChuteChanged。
        /// </summary>
        [Fact]
        public async Task SetForcedChuteAsync_ValidChute_ShouldWriteDoAndRaiseEvent() {
            var adapter = new FakeZhiQianClientAdapter();
            var manager = CreateManager(BuildValidOptions(), adapter);
            await manager.ConnectAsync();
            ForcedChuteChangedEventArgs? eventArgs = null;
            manager.ForcedChuteChanged += (_, args) => eventArgs = args;

            var result = await manager.SetForcedChuteAsync(101L);

            Assert.True(result);
            Assert.Equal(101L, manager.ForcedChuteId);
            Assert.NotNull(eventArgs);
            Assert.Null(eventArgs.Value.OldForcedChuteId);
            Assert.Equal(101L, eventArgs.Value.NewForcedChuteId);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// SetForcedChuteAsync 传入不在映射中的格口时应返回 false。
        /// </summary>
        [Fact]
        public async Task SetForcedChuteAsync_UnknownChute_ShouldReturnFalse() {
            var adapter = new FakeZhiQianClientAdapter();
            var manager = CreateManager(BuildValidOptions(), adapter);
            await manager.ConnectAsync();

            var result = await manager.SetForcedChuteAsync(999L);

            Assert.False(result);
            Assert.Null(manager.ForcedChuteId);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// SetForcedChuteAsync null 时应清除强排状态。
        /// </summary>
        [Fact]
        public async Task SetForcedChuteAsync_Null_ShouldClearForcedChute() {
            var adapter = new FakeZhiQianClientAdapter();
            var manager = CreateManager(BuildValidOptions(), adapter);
            await manager.ConnectAsync();
            await manager.SetForcedChuteAsync(101L);

            var result = await manager.SetForcedChuteAsync(null);

            Assert.True(result);
            Assert.Null(manager.ForcedChuteId);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 未连接时执行强排应被连接门控拦截并返回 false。
        /// </summary>
        [Fact]
        public async Task SetForcedChuteAsync_WhenNotConnected_ShouldReturnFalse() {
            var adapter = new FakeZhiQianClientAdapter();
            var manager = CreateManager(BuildValidOptions(), adapter);

            var result = await manager.SetForcedChuteAsync(101L);

            Assert.False(result);
            Assert.Null(manager.ForcedChuteId);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 已锁格目标执行强排应返回 false，避免锁格与强排冲突。
        /// </summary>
        [Fact]
        public async Task SetForcedChuteAsync_WhenTargetChuteLocked_ShouldReturnFalse() {
            var adapter = new FakeZhiQianClientAdapter();
            var manager = CreateManager(BuildValidOptions(), adapter);
            await manager.ConnectAsync();
            await manager.SetChuteLockedAsync(101L, true);

            var result = await manager.SetForcedChuteAsync(101L);

            Assert.False(result);
            Assert.Null(manager.ForcedChuteId);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// SetChuteLockedAsync 锁定时应将 DO 断开并更新锁格集合，触发 ChuteLockStatusChanged。
        /// </summary>
        [Fact]
        public async Task SetChuteLockedAsync_Lock_ShouldWriteOffAndUpdateLockedSet() {
            var adapter = new FakeZhiQianClientAdapter();
            adapter.SetDoState(1, true);
            var manager = CreateManager(BuildValidOptions(), adapter);
            await manager.ConnectAsync();
            var lockChanges = new List<ChuteLockStatusChangedEventArgs>();
            manager.ChuteLockStatusChanged += (_, args) => lockChanges.Add(args);

            var result = await manager.SetChuteLockedAsync(101L, true);

            Assert.True(result);
            Assert.Contains(101L, manager.LockedChuteIds);
            Assert.Contains(lockChanges, x => x.ChuteId == 101L && x.NewIsLocked);
            Assert.Contains(adapter.WriteHistory, x => x.DoIndex == 1 && !x.IsOn);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// SetChuteLockedAsync 解锁时应从锁格集合移除，触发 ChuteLockStatusChanged。
        /// </summary>
        [Fact]
        public async Task SetChuteLockedAsync_Unlock_ShouldRemoveFromLockedSet() {
            var adapter = new FakeZhiQianClientAdapter();
            var manager = CreateManager(BuildValidOptions(), adapter);
            await manager.ConnectAsync();
            await manager.SetChuteLockedAsync(101L, true);

            var result = await manager.SetChuteLockedAsync(101L, false);

            Assert.True(result);
            Assert.DoesNotContain(101L, manager.LockedChuteIds);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// AddTargetChuteAsync 应更新目标集合并触发 ChuteConfigurationChanged，且 IChute.IsTarget 为 true。
        /// </summary>
        [Fact]
        public async Task AddTargetChuteAsync_ValidChute_ShouldUpdateTargetSet() {
            var adapter = new FakeZhiQianClientAdapter();
            var manager = CreateManager(BuildValidOptions(), adapter);
            await manager.ConnectAsync();
            ChuteConfigurationChangedEventArgs? eventArgs = null;
            manager.ChuteConfigurationChanged += (_, args) => eventArgs = args;

            var result = await manager.AddTargetChuteAsync(101L);

            Assert.True(result);
            Assert.Contains(101L, manager.TargetChuteIds);
            Assert.NotNull(eventArgs);
            Assert.True(manager.TryGetChute(101L, out var chute) && chute.IsTarget);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// RemoveTargetChuteAsync 应从目标集合移除，且 IChute.IsTarget 变为 false。
        /// </summary>
        [Fact]
        public async Task RemoveTargetChuteAsync_ExistingTarget_ShouldRemoveFromSet() {
            var adapter = new FakeZhiQianClientAdapter();
            var manager = CreateManager(BuildValidOptions(), adapter);
            await manager.ConnectAsync();
            await manager.AddTargetChuteAsync(101L);

            var result = await manager.RemoveTargetChuteAsync(101L);

            Assert.True(result);
            Assert.DoesNotContain(101L, manager.TargetChuteIds);
            Assert.True(manager.TryGetChute(101L, out var chute) && !chute.IsTarget);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// TryGetChute 应从快照中返回正确格口，不触发设备通信。
        /// </summary>
        [Fact]
        public async Task TryGetChute_ExistingId_ShouldReturnChute() {
            var adapter = new FakeZhiQianClientAdapter();
            var manager = CreateManager(BuildValidOptions(), adapter);
            await manager.ConnectAsync();

            var found = manager.TryGetChute(101L, out var chute);

            Assert.True(found);
            Assert.Equal(101L, chute.Id);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// TryGetChute 对不存在的格口应返回 false。
        /// </summary>
        [Fact]
        public async Task TryGetChute_UnknownId_ShouldReturnFalse() {
            var adapter = new FakeZhiQianClientAdapter();
            var manager = CreateManager(BuildValidOptions(), adapter);
            await manager.ConnectAsync();

            var found = manager.TryGetChute(999L, out _);

            Assert.False(found);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 连接后 DO 状态应同步到 IChute.IoState。
        /// </summary>
        [Fact]
        public async Task ConnectAsync_ShouldSyncIoStatesToChutes() {
            var adapter = new FakeZhiQianClientAdapter();
            adapter.SetDoState(1, true);
            var manager = CreateManager(BuildValidOptions(), adapter);
            await manager.ConnectAsync();

            var found = manager.TryGetChute(101L, out var chute);

            Assert.True(found);
            Assert.Equal(IoState.High, chute.IoState);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 写 DO 失败时应发布 Faulted 事件并返回 false。
        /// </summary>
        [Fact]
        public async Task SetForcedChuteAsync_WriteFailure_ShouldRaiseFaultedEvent() {
            var adapter = new FakeZhiQianClientAdapter();
            var manager = CreateManager(BuildValidOptions(), adapter);
            await manager.ConnectAsync();
            adapter.ThrowOnWrite = true;
            var faulted = new List<ChuteManagerFaultedEventArgs>();
            manager.Faulted += (_, args) => faulted.Add(args);

            var result = await manager.SetForcedChuteAsync(101L);

            Assert.False(result);
            Assert.NotEmpty(faulted);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 时窗开关闸成功后应提交 Last 时窗并清空 Pending 时窗。
        /// </summary>
        [Fact]
        public async Task ScheduleChuteOpenWindowAsync_ShouldCommitPendingAndLastWindow() {
            var adapter = new FakeZhiQianClientAdapter();
            var manager = CreateManager(BuildValidOptions(), adapter);
            await manager.ConnectAsync();
            Assert.True(manager.TryGetChute(101L, out var chute));

            var openAt = GetLocalNow().AddMilliseconds(30);
            var closeAt = GetLocalNow().AddMilliseconds(90);
            var result = await InvokeScheduleChuteOpenWindowAsync(manager, 101L, openAt, closeAt);

            Assert.True(result);
            Assert.Null(chute.PendingIoOpenCloseWindow);
            Assert.NotNull(chute.LastIoOpenCloseWindow);
            Assert.Contains(adapter.WriteHistory, x => x is (1, true));
            Assert.Contains(adapter.WriteHistory, x => x is (1, false));
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 轮询连续读失败达到阈值后应自动重连并恢复 Connected。
        /// </summary>
        [Fact]
        public async Task PollLoop_ContinuousReadFailures_ShouldAutoReconnectAndRecoverConnected() {
            var options = BuildValidOptions();
            options.PollIntervalMs = 50;
            var adapter = new FakeZhiQianClientAdapter();
            var manager = CreateManager(options, adapter);
            await manager.ConnectAsync();
            adapter.ReadFailureCountRemaining = 3;

            var reconnected = await WaitUntilAsync(
                () => adapter.ConnectCount >= 2 && manager.ConnectionStatus == DeviceConnectionStatus.Connected,
                timeoutMs: 2000,
                intervalMs: 50);

            Assert.True(reconnected);
            Assert.True(adapter.DisconnectCount >= 1);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// DO 路号边界校验应与地址映射约束一致。
        /// </summary>
        [Fact]
        public void AddressMap_ValidateDoIndex_ShouldMatchBoundaries() {
            Assert.True(ZhiQianAddressMap.ValidateDoIndex(1));
            Assert.True(ZhiQianAddressMap.ValidateDoIndex(32));
            Assert.False(ZhiQianAddressMap.ValidateDoIndex(0));
            Assert.False(ZhiQianAddressMap.ValidateDoIndex(33));
        }

        /// <summary>
        /// ZhiQianChuteOptions.Validate 应拒绝 CommandTimeoutMs 小于 100 的配置。
        /// </summary>
        [Fact]
        public void Options_Validate_InvalidCommandTimeout_ShouldReturnErrors() {
            var options = BuildValidOptions();
            options.CommandTimeoutMs = 50;
            Assert.NotEmpty(options.Validate());
        }

        /// <summary>
        /// ZhiQianChuteOptions.Validate 合法配置应无错误。
        /// </summary>
        [Fact]
        public void Options_Validate_ValidConfig_ShouldReturnNoErrors() {
            var options = BuildValidOptions();
            Assert.Empty(options.Validate());
        }

        /// <summary>
        /// 创建供测试使用的 ZhiQianChuteManager 实例。
        /// </summary>
        /// <param name="options">格口配置。</param>
        /// <param name="adapter">内存适配器。</param>
        /// <returns>管理器实例。</returns>
        private static ZhiQianChuteManager CreateManager(ZhiQianChuteOptions options, FakeZhiQianClientAdapter adapter) {
            options.NormalizeLegacySingleDevice();
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var infraredDriverFrameCodec = new LeadshaineInfraredDriverFrameCodec(safeExecutor);
            return new ZhiQianChuteManager(options, options.Devices[0], adapter, safeExecutor, infraredDriverFrameCodec);
        }

        /// <summary>
        /// 构建指定 Y路映射的格口配置（用于参数化测试）。
        /// </summary>
        /// <param name="map">格口 Id → DO 编号映射。</param>
        /// <returns>格口配置实例。</returns>
        private static ZhiQianChuteOptions BuildOptions(Dictionary<long, int> map) =>
            new() {
                Enabled = true,
                CommandTimeoutMs = 300,
                RetryCount = 0,
                RetryDelayMs = 10,
                PollIntervalMs = 50,
                DefaultOpenDurationMs = 120,
                ForceOpenExclusive = true,
                Host = "192.168.1.199",
                Port = 1030,
                DeviceAddress = 1,
                ChuteToDoMap = map
            };

        /// <summary>
        /// 构建含合法默认 Y路映射（101→1、102→2、103→3）的格口配置。
        /// </summary>
        /// <returns>格口配置实例。</returns>
        private static ZhiQianChuteOptions BuildValidOptions() =>
            BuildOptions(new Dictionary<long, int> { { 101L, 1 }, { 102L, 2 }, { 103L, 3 } });

        /// <summary>
        /// 通过反射调用管理器内部时窗调度方法（仅测试使用）。
        /// 由于目标方法为 internal，为避免扩大生产代码可见性，测试侧通过反射触发。
        /// </summary>
        /// <param name="manager">管理器实例。</param>
        /// <param name="chuteId">格口 Id。</param>
        /// <param name="openAt">开闸本地时间。</param>
        /// <param name="closeAt">关闸本地时间。</param>
        /// <returns>调度执行结果。</returns>
        private static async Task<bool> InvokeScheduleChuteOpenWindowAsync(
            ZhiQianChuteManager manager,
            long chuteId,
            DateTime openAt,
            DateTime closeAt) {
            var method = typeof(ZhiQianChuteManager).GetMethod(
                "ScheduleChuteOpenWindowAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var valueTask = (ValueTask<bool>)method!.Invoke(
                manager,
                new object?[] { chuteId, openAt, closeAt, CancellationToken.None })!;
            return await valueTask.ConfigureAwait(false);
        }

        /// <summary>
        /// 在超时时间内等待断言条件成立。
        /// 每隔 intervalMs 轮询一次 predicate，超时后返回最后一次 predicate 计算结果。
        /// </summary>
        /// <param name="predicate">断言条件。</param>
        /// <param name="timeoutMs">超时毫秒。</param>
        /// <param name="intervalMs">轮询间隔毫秒。</param>
        /// <returns>条件是否在超时前成立。</returns>
        private static async Task<bool> WaitUntilAsync(Func<bool> predicate, int timeoutMs, int intervalMs) {
            var deadline = GetLocalNow().AddMilliseconds(timeoutMs);
            while (GetLocalNow() <= deadline) {
                if (predicate()) {
                    return true;
                }

                await Task.Delay(intervalMs).ConfigureAwait(false);
            }

            return predicate();
        }

        /// <summary>
        /// 获取当前本地时间（测试场景统一本地时间语义）。
        /// </summary>
        /// <returns>当前本地时间。</returns>
        private static DateTime GetLocalNow() {
            return DateTime.Now;
        }
    }
}
