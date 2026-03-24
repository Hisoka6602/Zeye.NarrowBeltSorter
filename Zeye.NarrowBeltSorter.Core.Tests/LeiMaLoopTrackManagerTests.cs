using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Events.Track;
using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// 雷玛环形轨道管理器行为测试。
    /// </summary>
    public sealed class LeiMaLoopTrackManagerTests {
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
            Assert.Equal((ushort)500, adapter.LastWriteValue);
            Assert.Equal(LeiMaRegisters.TorqueSetpoint, adapter.LastWriteAddress);
            Assert.Equal(2500m, manager.TargetSpeedMmps);
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
            Assert.Equal("LeiMa.SetTargetSpeedAsync", fault.Value.Operation);
            Assert.IsType<InvalidOperationException>(fault.Value.Exception);
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
            decimal maxOutputHz = 50m,
            ushort maxTorqueRawUnit = 1000) {
            adapter.SetReadValue(LeiMaRegisters.RunStatus, 3);
            adapter.SetReadValue(LeiMaRegisters.AlarmCode, 0);
            adapter.SetReadValue(LeiMaRegisters.EncoderFeedbackSpeed, 0);
            adapter.SetReadValue(LeiMaRegisters.RunningFrequency, 0);

            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);

            return new LeiMaLoopTrackManager(
                trackName: "LeiMa-Test-Track",
                modbusClient: adapter,
                safeExecutor: safeExecutor,
                connectionOptions: new LoopTrackConnectionOptions(),
                pidOptions: new LoopTrackPidOptions(),
                maxOutputHz: maxOutputHz,
                maxTorqueRawUnit: maxTorqueRawUnit,
                pollingInterval: TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// 雷玛 Modbus 测试桩。
        /// </summary>
        private sealed class FakeLeiMaModbusClientAdapter : ILeiMaModbusClientAdapter {
            private readonly Dictionary<ushort, ushort> _readValues = new();

            public bool IsConnected { get; private set; }

            public (ushort Address, ushort Value) LastWrite => Writes.Count == 0 ? default : Writes[^1];

            public ushort LastWriteAddress => LastWrite.Address;

            public ushort LastWriteValue => LastWrite.Value;

            public List<(ushort Address, ushort Value)> Writes { get; } = new();

            public Exception? ThrowOnWrite { get; set; }

            public ValueTask ConnectAsync(CancellationToken cancellationToken = default) {
                IsConnected = true;
                return ValueTask.CompletedTask;
            }

            public ValueTask DisconnectAsync(CancellationToken cancellationToken = default) {
                IsConnected = false;
                return ValueTask.CompletedTask;
            }

            public ValueTask<ushort> ReadHoldingRegisterAsync(ushort address, CancellationToken cancellationToken = default) {
                _readValues.TryGetValue(address, out var value);
                return ValueTask.FromResult(value);
            }

            public ValueTask WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken cancellationToken = default) {
                if (ThrowOnWrite is not null) {
                    throw ThrowOnWrite;
                }

                Writes.Add((address, value));
                return ValueTask.CompletedTask;
            }

            public void SetReadValue(ushort address, ushort value) {
                _readValues[address] = value;
            }

            public ValueTask DisposeAsync() {
                IsConnected = false;
                return ValueTask.CompletedTask;
            }
        }
    }
}
