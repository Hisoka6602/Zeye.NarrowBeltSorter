using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Utilities.Chutes;

namespace Zeye.NarrowBeltSorter.Core.Tests {

    /// <summary>
    /// 智嵌客户端适配器测试桩（内存 DO 读写，供单元测试使用）。
    /// </summary>
    internal sealed class FakeZhiQianClientAdapter : IZhiQianClientAdapter {
        private readonly bool[] _doStates = new bool[ZhiQianAddressMap.DoChannelCount];
        private readonly List<(int DoIndex, bool IsOn)> _writeHistory = new();
        private readonly List<byte[]> _infraredWriteHistory = new();

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
        /// 红外帧写入历史。
        /// </summary>
        public IReadOnlyList<byte[]> InfraredWriteHistory => _infraredWriteHistory;

        /// <summary>
        /// 设置指定 Y 路的 DO 状态（用于测试验证）。
        /// </summary>
        /// <param name="doIndex">Y 路编号（1~32）。</param>
        /// <param name="isOn">目标状态。</param>
        public void SetDoState(int doIndex, bool isOn) {
            _doStates[doIndex - ZhiQianAddressMap.DoIndexMin] = isOn;
        }

        /// <summary>
        /// 是否在连接时抛出异常（用于测试连接失败场景）。
        /// </summary>
        public bool ThrowOnConnect { get; set; }

        /// <summary>
        /// 是否在写 DO 时抛出异常（用于测试通信失败场景）。
        /// </summary>
        public bool ThrowOnWrite { get; set; }

        /// <summary>
        /// 剩余读失败次数（大于 0 时每次 ReadDoStatesAsync 抛异常并递减）。
        /// </summary>
        public int ReadFailureCountRemaining { get; set; }

        /// <summary>
        /// 是否在写入时忽略状态更新（用于模拟写后读持续不一致）。
        /// </summary>
        public bool IgnoreWriteStateUpdate { get; set; }

        /// <inheritdoc />
        public ValueTask ConnectAsync(CancellationToken cancellationToken = default) {
            if (ThrowOnConnect) {
                throw new InvalidOperationException("连接失败（ThrowOnConnect=true）。");
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
            if (ReadFailureCountRemaining > 0) {
                ReadFailureCountRemaining--;
                throw new InvalidOperationException("读 DO 失败（ReadFailureCountRemaining>0）。");
            }

            return ValueTask.FromResult<IReadOnlyList<bool>>(_doStates.ToArray());
        }

        /// <inheritdoc />
        public ValueTask WriteSingleDoAsync(int doIndex, bool isOn, CancellationToken cancellationToken = default) {
            if (ThrowOnWrite) {
                throw new InvalidOperationException("写 DO 失败（ThrowOnWrite=true）。");
            }

            _writeHistory.Add((doIndex, isOn));
            if (!IgnoreWriteStateUpdate) {
                _doStates[doIndex - ZhiQianAddressMap.DoIndexMin] = isOn;
            }

            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask WriteBatchDoAsync(IReadOnlyDictionary<int, bool> doStates, CancellationToken cancellationToken = default) {
            if (ThrowOnWrite) {
                throw new InvalidOperationException("批量写 DO 失败（ThrowOnWrite=true）。");
            }

            foreach (var (doIndex, isOn) in doStates) {
                _writeHistory.Add((doIndex, isOn));
                if (!IgnoreWriteStateUpdate) {
                    _doStates[doIndex - ZhiQianAddressMap.DoIndexMin] = isOn;
                }
            }

            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask WriteInfraredFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default) {
            if (ThrowOnWrite) {
                throw new InvalidOperationException("写红外帧失败（ThrowOnWrite=true）。");
            }

            _infraredWriteHistory.Add(frame.ToArray());
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }
}
