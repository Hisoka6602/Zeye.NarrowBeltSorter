using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.Track;
using Zeye.NarrowBeltSorter.Core.Events.Track;
using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa;
using Zeye.NarrowBeltSorter.Core.Utilities.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;

namespace Zeye.NarrowBeltSorter.Core.Tests {

    /// <summary>
    /// 雷玛环形轨道管理器行为测试。
    /// </summary>
    public sealed class LeiMaLoopTrackManagerTests {
        private static readonly TimeSpan StopAsyncPostPollingWindow = TimeSpan.FromMilliseconds(350);

        /// <summary>
        /// 连接与断连应正确触发状态流转。
        /// </summary>
        [Fact]
        public async Task ConnectDisconnect_ShouldTransitConnectionStatus() {
            var adapter = new FakeLeiMaModbusClientAdapter();
            var manager = CreateManager(adapter);
            var states = new List<(int OldValue, int NewValue)>();
            manager.ConnectionStatusChanged += (_, args) => states.Add(((int)args.OldStatus, (int)args.NewStatus));

            var connected = await manager.ConnectAsync();
            await manager.DisconnectAsync();

            Assert.True(connected);
            Assert.Contains(states, x => x.OldValue == 0 && x.NewValue == 1);
            Assert.Contains(states, x => x.OldValue == 1 && x.NewValue == 2);
            Assert.Contains(states, x => x.OldValue == 2 && x.NewValue == 0);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 目标速度应执行 mm/s->Hz->P3.10 转矩原始值换算并写寄存器。
        /// </summary>
        [Fact]
        public async Task SetTargetSpeed_ShouldConvertMmpsToTorqueRegisterValue() {
            var adapter = new FakeLeiMaModbusClientAdapter();
            var manager = CreateManager(adapter, maxOutputHz: 50m, maxTorqueRawUnit: 1000);
            await manager.ConnectAsync();

            var setResult = await manager.SetTargetSpeedAsync(2500m);

            Assert.True(setResult);
            // 新行为：设速阶段写入最大转矩上限（启动满扭），由 PID 闭环后续调节。
            Assert.Equal((ushort)1000, adapter.LastWriteValue);
            Assert.Equal(LeiMaRegisters.TorqueSetpoint, adapter.LastWriteAddress);
            Assert.DoesNotContain(adapter.Writes, x => x.Address == LeiMaRegisters.FrequencySetpoint);
            Assert.Equal(2500m, manager.TargetSpeedMmps);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// PID 闭环应基于反馈更新 P/I/D 并继续写入 P3.10 主链路。
        /// </summary>
        [Fact]
        public async Task PidClosedLoop_ShouldUpdatePidStateAndWriteTorqueSetpoint() {
            // 步骤1：构造启用 PID 的管理器与反馈输入，模拟运行态闭环场景。
            var adapter = new FakeLeiMaModbusClientAdapter();
            var manager = CreateManager(
                adapter,
                maxOutputHz: 25m,
                maxTorqueRawUnit: 1000,
                pid: new LoopTrackPidOptions {
                    Enabled = true,
                    Kp = 0.5m,
                    Ki = 0.2m,
                    Kd = 0.1m,
                    OutputMinRaw = 0m,
                    OutputMaxRaw = 25m,
                    IntegralMin = -20m,
                    IntegralMax = 20m,
                    DerivativeFilterAlpha = 0.2m,
                    FreezeIntegralWhenNotRunning = true
                });

            adapter.SetReadValue(LeiMaRegisters.RunStatus, 1);
            adapter.SetReadValue(LeiMaRegisters.AlarmCode, 0);
            adapter.SetReadValue(LeiMaRegisters.EncoderFeedbackSpeed, 300);
            adapter.SetReadValue(LeiMaRegisters.RunningFrequency, 300);
            await manager.ConnectAsync();
            await manager.StartAsync();

            // 步骤2：下发目标速度并等待轮询线程至少执行一次闭环计算。
            var setResult = await manager.SetTargetSpeedAsync(1200m);
            await Task.Delay(450);

            // 步骤3：验证 PID 状态与 P3.10 写入主链路均按预期生效。
            Assert.True(setResult);
            Assert.True(manager.PidLastUpdatedAt.HasValue);
            Assert.NotEqual(0m, manager.PidLastErrorMmps);
            Assert.Contains(adapter.Writes, x => x.Address == LeiMaRegisters.TorqueSetpoint);
            Assert.DoesNotContain(adapter.Writes, x => x.Address == LeiMaRegisters.FrequencySetpoint);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 非运行状态不应执行 PID 输出写入。
        /// </summary>
        [Fact]
        public async Task PidClosedLoop_WhenNotRunning_ShouldNotWriteTorqueSetpoint() {
            var adapter = new FakeLeiMaModbusClientAdapter();
            var manager = CreateManager(adapter);
            adapter.SetReadValue(LeiMaRegisters.RunStatus, 3);
            adapter.SetReadValue(LeiMaRegisters.AlarmCode, 0);
            adapter.SetReadValue(LeiMaRegisters.EncoderFeedbackSpeed, 300);
            adapter.SetReadValue(LeiMaRegisters.RunningFrequency, 300);
            await manager.ConnectAsync();
            var setResult = await manager.SetTargetSpeedAsync(1000m);
            var beforeWaitWrites = adapter.Writes.Count(x => x.Address == LeiMaRegisters.TorqueSetpoint);
            await Task.Delay(350);
            var afterWaitWrites = adapter.Writes.Count(x => x.Address == LeiMaRegisters.TorqueSetpoint);

            Assert.True(setResult);
            Assert.Equal(beforeWaitWrites, afterWaitWrites);
            Assert.Null(manager.PidLastUpdatedAt);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// P3.10 写入节流应严格遵守最小间隔，不因值变化提前写入。
        /// </summary>
        [Fact]
        public async Task PidClosedLoop_WriteThrottle_ShouldRespectMinimumInterval() {
            var adapter = new FakeLeiMaModbusClientAdapter();
            adapter.SetReadValue(LeiMaRegisters.RunStatus, 1);
            adapter.SetReadValue(LeiMaRegisters.AlarmCode, 0);
            adapter.SetReadValue(LeiMaRegisters.EncoderFeedbackSpeed, 100);
            adapter.SetReadValue(LeiMaRegisters.RunningFrequency, 100);
            var manager = CreateManager(
                adapter,
                pid: new LoopTrackPidOptions {
                    Enabled = true,
                    Kp = 0.8m,
                    Ki = 0.2m,
                    Kd = 0m,
                    OutputMinRaw = 0m,
                    OutputMaxRaw = 25m,
                    IntegralMin = -20m,
                    IntegralMax = 20m,
                    DerivativeFilterAlpha = 0.2m,
                    FreezeIntegralWhenNotRunning = true
                },
                pollingInterval: TimeSpan.FromMilliseconds(50),
                writeInterval: TimeSpan.FromMilliseconds(300));

            await manager.ConnectAsync();
            await manager.StartAsync();
            var beforeSetWrites = adapter.Writes.Count(x => x.Address == LeiMaRegisters.TorqueSetpoint);
            _ = await manager.SetTargetSpeedAsync(1200m);
            var shortWindowDelay = TimeSpan.FromMilliseconds(190);
            var fullWindowDelay = TimeSpan.FromMilliseconds(260);
            // 步骤1：先等待小于写入间隔的窗口，确认不会发生超频写入。
            await Task.Delay(shortWindowDelay);
            var shortWindowWrites = adapter.Writes.Count(x => x.Address == LeiMaRegisters.TorqueSetpoint) - beforeSetWrites;
            // 步骤2：再补充等待，使总等待超过写入间隔，确认下一次写入按节流触发。
            await Task.Delay(fullWindowDelay);
            var fullWindowWrites = adapter.Writes.Count(x => x.Address == LeiMaRegisters.TorqueSetpoint) - beforeSetWrites;

            Assert.InRange(shortWindowWrites, 0, 1);
            Assert.InRange(fullWindowWrites, 1, 2);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 启停与复位应写入正确命令字。
        /// </summary>
        [Fact]
        public async Task StartStopClearAlarm_ShouldWriteCommandWords() {
            var adapter = new FakeLeiMaModbusClientAdapter();
            adapter.SetReadValue(LeiMaRegisters.AlarmCode, 0);
            var manager = CreateManager(adapter);
            await manager.ConnectAsync();

            var startOk = await manager.StartAsync();
            var stopOk = await manager.StopAsync();
            var clearOk = await manager.ClearAlarmAsync();

            Assert.True(startOk);
            Assert.True(stopOk);
            Assert.True(clearOk);
            Assert.Contains(adapter.Writes, x => x.Address == LeiMaRegisters.Command && x.Value == LeiMaRegisters.CommandForwardRun);
            Assert.Contains(adapter.Writes, x => x.Address == LeiMaRegisters.Command && x.Value == LeiMaRegisters.CommandDecelerateStop);
            Assert.Contains(adapter.Writes, x => x.Address == LeiMaRegisters.Command && x.Value == LeiMaRegisters.CommandAlarmReset);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 危险调用异常必须隔离，并触发 Faulted 事件。
        /// </summary>
        [Fact]
        public async Task UnsafeCallException_ShouldBeIsolatedAndPublishFaultedEvent() {
            var adapter = new FakeLeiMaModbusClientAdapter {
                ThrowOnWrite = new InvalidOperationException("写寄存器失败")
            };
            var manager = CreateManager(adapter);
            await manager.ConnectAsync();

            LoopTrackManagerFaultedEventArgs? fault = null;
            manager.Faulted += (_, args) => fault = args;

            var result = await manager.SetTargetSpeedAsync(1000m);

            Assert.False(result);
            Assert.True(fault.HasValue);
            Assert.Equal("LeiMa.SetTargetSpeedAsync.Slave1", fault.Value.Operation);
            Assert.IsType<InvalidOperationException>(fault.Value.Exception);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// RunStatus 回读异常时，设速应依赖缓存状态继续执行，不拦截写入 P3.10。
        /// </summary>
        [Fact]
        public async Task SetTargetSpeed_WhenRunStatusReadFailed_ShouldProceedWithCachedStatus() {
            var adapter = new FakeLeiMaModbusClientAdapter();
            adapter.SetReadException(LeiMaRegisters.RunStatus, new InvalidOperationException("运行状态读取失败"));
            var manager = CreateManager(adapter);
            await manager.ConnectAsync();

            var result = await manager.SetTargetSpeedAsync(1000m);

            Assert.True(result);
            Assert.Contains(adapter.Writes, x => x.Address == LeiMaRegisters.TorqueSetpoint);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// StopAsync 成功后，轮询采样不应继续触发稳速重置事件。
        /// </summary>
        [Fact]
        public async Task StopAsync_AfterSucceeded_ShouldSuspendStabilizationMonitoring() {
            var adapter = CreateSlaveAdapter(0);
            var manager = CreateManager(adapter);
            var resetEventCount = 0;
            manager.StabilizationReset += (_, _) => resetEventCount++;
            await manager.ConnectAsync();
            _ = await manager.StartAsync();
            _ = await manager.SetTargetSpeedAsync(1000m);
            _ = await manager.StopAsync();
            var resetCountAfterStop = resetEventCount;

            // 额外等待超过默认轮询周期（100ms），验证 StopAsync 后不会继续新增稳速重置事件。
            await Task.Delay(StopAsyncPostPollingWindow);

            Assert.Equal(resetCountAfterStop, resetEventCount);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 非 StopAsync 场景下，目标速度变化仍应触发稳速重置事件。
        /// </summary>
        [Fact]
        public async Task SetTargetSpeed_WhenNotRunning_ShouldStillRaiseStabilizationReset() {
            var adapter = CreateSlaveAdapter(0);
            var manager = CreateManager(adapter);
            var resetEventCount = 0;
            manager.StabilizationReset += (_, _) => resetEventCount++;
            await manager.ConnectAsync();

            _ = await manager.SetTargetSpeedAsync(1000m);

            Assert.True(resetEventCount > 0);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 多从站最小值策略应输出最小速度聚合值。
        /// </summary>
        [Fact]
        public async Task PollingMultiSlave_MinStrategy_ShouldUseMinSpeed() {
            var slave1 = CreateSlaveAdapter(1000);
            var slave2 = CreateSlaveAdapter(2500);
            var manager = CreateManager(slave1, slave2, SpeedAggregateStrategy.Min);
            await manager.ConnectAsync();
            _ = await manager.StartAsync();
            _ = await manager.SetTargetSpeedAsync(1000m);
            await Task.Delay(520);

            Assert.Equal(1000m, manager.RealTimeSpeedMmps);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 多从站平均策略应输出平均速度聚合值。
        /// </summary>
        [Fact]
        public async Task PollingMultiSlave_AvgStrategy_ShouldUseAverageSpeed() {
            var slave1 = CreateSlaveAdapter(1000);
            var slave2 = CreateSlaveAdapter(2000);
            var manager = CreateManager(slave1, slave2, SpeedAggregateStrategy.Avg);
            await manager.ConnectAsync();
            _ = await manager.StartAsync();
            _ = await manager.SetTargetSpeedAsync(1000m);
            await Task.Delay(520);

            Assert.Equal(1500m, manager.RealTimeSpeedMmps);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 多从站中位数策略应输出中位速度聚合值。
        /// </summary>
        [Fact]
        public async Task PollingMultiSlave_MedianStrategy_ShouldUseMedianSpeed() {
            var slave1 = CreateSlaveAdapter(800);
            var slave2 = CreateSlaveAdapter(1500);
            var slave3 = CreateSlaveAdapter(3000);
            var manager = CreateManager(slave1, slave2, slave3, SpeedAggregateStrategy.Median);
            await manager.ConnectAsync();
            _ = await manager.StartAsync();
            _ = await manager.SetTargetSpeedAsync(1000m);
            await Task.Delay(520);

            Assert.Equal(1500m, manager.RealTimeSpeedMmps);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 多从站部分失败时应触发部分失败事件并仍可输出聚合速度。
        /// </summary>
        [Fact]
        public async Task PollingMultiSlave_WhenPartialFail_ShouldEmitEventAndAggregateSpeed() {
            var successAdapter = CreateSlaveAdapter(1200);
            var failedAdapter = CreateSlaveAdapter(0);
            failedAdapter.SetReadException(LeiMaRegisters.EncoderFeedbackSpeed, new InvalidOperationException("从站读取失败"));
            var manager = CreateManager(successAdapter, failedAdapter, SpeedAggregateStrategy.Min);
            LoopTrackSpeedSamplingPartiallyFailedEventArgs? partialFail = null;
            manager.SpeedSamplingPartiallyFailed += (_, args) => partialFail = args;
            await manager.ConnectAsync();
            _ = await manager.StartAsync();
            _ = await manager.SetTargetSpeedAsync(1000m);
            await Task.Delay(520);

            Assert.True(partialFail.HasValue);
            Assert.Equal(1, partialFail.Value.SuccessCount);
            Assert.Equal(1, partialFail.Value.FailCount);
            Assert.Equal("2", partialFail.Value.FailedSlaveIds);
            Assert.Equal(1200m, manager.RealTimeSpeedMmps);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 多从站全部失败时应保持实时速度不更新。
        /// </summary>
        [Fact]
        public async Task PollingMultiSlave_WhenAllFail_ShouldKeepRealtimeSpeed() {
            var failedAdapter1 = CreateSlaveAdapter(0);
            var failedAdapter2 = CreateSlaveAdapter(0);
            failedAdapter1.ThrowOnRead = new InvalidOperationException("从站1读取失败");
            failedAdapter2.ThrowOnRead = new InvalidOperationException("从站2读取失败");
            var manager = CreateManager(failedAdapter1, failedAdapter2, SpeedAggregateStrategy.Min);
            await manager.ConnectAsync();
            _ = await manager.StartAsync();
            _ = await manager.SetTargetSpeedAsync(1000m);
            await Task.Delay(520);

            Assert.Equal(0m, manager.RealTimeSpeedMmps);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 多从站写入广播任一失败应返回 false 并触发故障事件。
        /// </summary>
        [Fact]
        public async Task WriteBroadcast_WhenAnySlaveFail_ShouldReturnFalse() {
            var successAdapter = CreateSlaveAdapter(120);
            var failedAdapter = CreateSlaveAdapter(120);
            failedAdapter.ThrowOnWrite = new InvalidOperationException("写入失败");
            var manager = CreateManager(successAdapter, failedAdapter, SpeedAggregateStrategy.Min);
            await manager.ConnectAsync();
            var startResult = await manager.StartAsync();
            var setResult = await manager.SetTargetSpeedAsync(1000m);
            var stopResult = await manager.StopAsync();

            Assert.False(startResult);
            Assert.False(setResult);
            Assert.False(stopResult);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 多从站连接与断连应覆盖全部从站。
        /// </summary>
        [Fact]
        public async Task MultiSlave_ConnectAndDisconnect_ShouldCoverAllConfiguredSlaves() {
            var slave1 = CreateSlaveAdapter(120);
            var slave2 = CreateSlaveAdapter(120);
            var slave3 = CreateSlaveAdapter(120);
            var manager = CreateManager(slave1, slave2, slave3, SpeedAggregateStrategy.Min);

            var connected = await manager.ConnectAsync();
            await manager.DisconnectAsync();

            Assert.True(connected);
            Assert.Equal(1, slave1.ConnectCallCount);
            Assert.Equal(1, slave2.ConnectCallCount);
            Assert.Equal(1, slave3.ConnectCallCount);
            Assert.Equal(1, slave1.DisconnectCallCount);
            Assert.Equal(1, slave2.DisconnectCallCount);
            Assert.Equal(1, slave3.DisconnectCallCount);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 多从站清报警应广播到全部从站并逐个回读故障码。
        /// </summary>
        [Fact]
        public async Task ClearAlarm_WhenMultiSlave_ShouldBroadcastAndReadBackAllSlaves() {
            var slave1 = CreateSlaveAdapter(120);
            var slave2 = CreateSlaveAdapter(120);
            var slave3 = CreateSlaveAdapter(120);
            var manager = CreateManager(slave1, slave2, slave3, SpeedAggregateStrategy.Min);
            await manager.ConnectAsync();

            var clearOk = await manager.ClearAlarmAsync();

            Assert.True(clearOk);
            Assert.Contains(slave1.Writes, x => x.Address == LeiMaRegisters.Command && x.Value == LeiMaRegisters.CommandAlarmReset);
            Assert.Contains(slave2.Writes, x => x.Address == LeiMaRegisters.Command && x.Value == LeiMaRegisters.CommandAlarmReset);
            Assert.Contains(slave3.Writes, x => x.Address == LeiMaRegisters.Command && x.Value == LeiMaRegisters.CommandAlarmReset);
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 创建管理器实例。
        /// </summary>
        /// <param name="adapter">测试适配器。</param>
        /// <param name="maxOutputHz">最大频率。</param>
        /// <param name="maxTorqueRawUnit">最大转矩原始值。</param>
        /// <returns>管理器实例。</returns>
        private static LeiMaLoopTrackManager CreateManager(
            FakeLeiMaModbusClientAdapter adapter,
            decimal maxOutputHz = 25m,
            ushort maxTorqueRawUnit = 1000,
            LoopTrackPidOptions? pid = null,
            TimeSpan? pollingInterval = null,
            TimeSpan? writeInterval = null) {
            adapter.SetReadValue(LeiMaRegisters.RunStatus, 3);
            adapter.SetReadValue(LeiMaRegisters.AlarmCode, 0);
            adapter.SetReadValue(LeiMaRegisters.EncoderFeedbackSpeed, 0);
            adapter.SetReadValue(LeiMaRegisters.RunningFrequency, 0);

            var safeExecutor = (SafeExecutor)Activator.CreateInstance(
                typeof(SafeExecutor),
                NullLogger<SafeExecutor>.Instance)!;

            return new LeiMaLoopTrackManager(
                trackName: "LeiMa-Test-Track",
                modbusClient: adapter,
                safeExecutor: safeExecutor,
                connectionOptions: new LoopTrackConnectionOptions(),
                pidOptions: pid ?? new LoopTrackPidOptions(),
                maxOutputHz: maxOutputHz,
                maxTorqueRawUnit: maxTorqueRawUnit,
                pollingInterval: pollingInterval ?? TimeSpan.FromMilliseconds(100),
                torqueSetpointWriteInterval: writeInterval ?? TimeSpan.FromMilliseconds(100));
        }

        /// <summary>
        /// 创建多从站管理器。
        /// </summary>
        /// <param name="adapters">从站适配器集合。</param>
        /// <param name="strategy">速度聚合策略。</param>
        /// <returns>管理器实例。</returns>
        private static LeiMaLoopTrackManager CreateManager(
            FakeLeiMaModbusClientAdapter adapter1,
            FakeLeiMaModbusClientAdapter adapter2,
            SpeedAggregateStrategy strategy) {
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            InitializeDefaults(adapter1);
            InitializeDefaults(adapter2);
            return new LeiMaLoopTrackManager(
                trackName: "LeiMa-Test-Track",
                modbusClient: adapter1,
                safeExecutor: safeExecutor,
                connectionOptions: new LoopTrackConnectionOptions(),
                pidOptions: new LoopTrackPidOptions(),
                pollingInterval: TimeSpan.FromMilliseconds(100),
                torqueSetpointWriteInterval: TimeSpan.FromMilliseconds(100),
                slaveClients: [(1, adapter1), (2, adapter2)],
                speedAggregateStrategy: strategy);
        }

        /// <summary>
        /// 创建三从站管理器。
        /// </summary>
        /// <param name="adapter1">从站1。</param>
        /// <param name="adapter2">从站2。</param>
        /// <param name="adapter3">从站3。</param>
        /// <param name="strategy">策略。</param>
        /// <returns>管理器。</returns>
        private static LeiMaLoopTrackManager CreateManager(
            FakeLeiMaModbusClientAdapter adapter1,
            FakeLeiMaModbusClientAdapter adapter2,
            FakeLeiMaModbusClientAdapter adapter3,
            SpeedAggregateStrategy strategy) {
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            InitializeDefaults(adapter1);
            InitializeDefaults(adapter2);
            InitializeDefaults(adapter3);
            return new LeiMaLoopTrackManager(
                trackName: "LeiMa-Test-Track",
                modbusClient: adapter1,
                safeExecutor: safeExecutor,
                connectionOptions: new LoopTrackConnectionOptions(),
                pidOptions: new LoopTrackPidOptions(),
                pollingInterval: TimeSpan.FromMilliseconds(100),
                torqueSetpointWriteInterval: TimeSpan.FromMilliseconds(100),
                slaveClients: [(1, adapter1), (2, adapter2), (3, adapter3)],
                speedAggregateStrategy: strategy);
        }

        /// <summary>
        /// 创建带编码器反馈的从站适配器。
        /// </summary>
        /// <param name="encoderRaw">编码器原始值。</param>
        /// <returns>适配器。</returns>
        private static FakeLeiMaModbusClientAdapter CreateSlaveAdapter(ushort encoderRaw) {
            var adapter = new FakeLeiMaModbusClientAdapter();
            InitializeDefaults(adapter);
            adapter.SetReadValue(LeiMaRegisters.EncoderFeedbackSpeed, encoderRaw);
            return adapter;
        }

        /// <summary>
        /// 初始化测试默认寄存器值。
        /// </summary>
        /// <param name="adapter">适配器。</param>
        private static void InitializeDefaults(FakeLeiMaModbusClientAdapter adapter) {
            adapter.SetReadValue(LeiMaRegisters.RunStatus, 1);
            adapter.SetReadValue(LeiMaRegisters.AlarmCode, 0);
        }
    }
}
