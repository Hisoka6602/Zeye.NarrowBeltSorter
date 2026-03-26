using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Utilities.Chutes;

namespace Zeye.NarrowBeltSorter.Core.Tests {

    /// <summary>
    /// 智嵌 Modbus 客户端适配器测试桩（模拟连接与 DO 读写）。
    /// </summary>
    internal sealed class FakeZhiQianModbusClientAdapter : IZhiQianModbusClientAdapter {
        private readonly bool[] _doStates = new bool[ZhiQianAddressMap.DoChannelCount];
        private readonly List<(int DoIndex, bool IsOn)> _writeHistory = new();

        /// <summary>
        /// 当前连接状态（由测试控制）。
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// 连接次数（用于断言调用计数）。
        /// </summary>
        public int ConnectCount { get; private set; }

        /// <summary>
        /// 断开次数（用于断言调用计数）。
        /// </summary>
        public int DisconnectCount { get; private set; }

        /// <summary>
        /// 全量单路写入历史（DoIndex, IsOn）。
        /// </summary>
        public IReadOnlyList<(int DoIndex, bool IsOn)> WriteHistory => _writeHistory;

        /// <summary>
        /// 设置指定 Y 路的 DO 状态（用于测试验证）。
        /// </summary>
        /// <param name="doIndex">Y 路编号（1~32）。</param>
        /// <param name="isOn">目标状态。</param>
        public void SetDoState(int doIndex, bool isOn) {
            _doStates[doIndex - ZhiQianAddressMap.DoIndexMin] = isOn;
        }

        /// <summary>
        /// 是否在连接时抛出异常（模拟连接失败场景）。
        /// </summary>
        public bool ThrowOnConnect { get; set; }

        /// <summary>
        /// 是否在写 DO 时抛出异常（模拟通信失败场景）。
        /// </summary>
        public bool ThrowOnWrite { get; set; }

        /// <inheritdoc />
        public ValueTask ConnectAsync(CancellationToken cancellationToken = default) {
            if (ThrowOnConnect) {
                throw new InvalidOperationException("模拟连接失败。");
            }

            ConnectCount++;
            IsConnected = true;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default) {
            DisconnectCount++;
            IsConnected = false;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask<IReadOnlyList<bool>> ReadDoStatesAsync(CancellationToken cancellationToken = default) {
            return ValueTask.FromResult<IReadOnlyList<bool>>(_doStates.ToArray());
        }

        /// <inheritdoc />
        public ValueTask WriteSingleDoAsync(int doIndex, bool isOn, CancellationToken cancellationToken = default) {
            if (ThrowOnWrite) {
                throw new InvalidOperationException("模拟写 DO 失败。");
            }

            _writeHistory.Add((doIndex, isOn));
            _doStates[doIndex - ZhiQianAddressMap.DoIndexMin] = isOn;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask WriteBatchDoAsync(IReadOnlyDictionary<int, bool> doStates, CancellationToken cancellationToken = default) {
            if (ThrowOnWrite) {
                throw new InvalidOperationException("模拟批量写 DO 失败。");
            }

            foreach (var (doIndex, isOn) in doStates) {
                _writeHistory.Add((doIndex, isOn));
                _doStates[doIndex - ZhiQianAddressMap.DoIndexMin] = isOn;
            }

            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }
}
