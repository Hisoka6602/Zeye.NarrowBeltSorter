using Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// 雷码 Modbus 测试桩。
    /// </summary>
    internal sealed class FakeLeiMaModbusClientAdapter : ILeiMaModbusClientAdapter {
        private readonly Dictionary<ushort, ushort> _readValues = new();

        /// <summary>
        /// 是否已连接。
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// 最新写入项。
        /// </summary>
        public (ushort Address, ushort Value) LastWrite => Writes.Count == 0 ? default : Writes[^1];

        /// <summary>
        /// 最新写入地址。
        /// </summary>
        public ushort LastWriteAddress => LastWrite.Address;

        /// <summary>
        /// 最新写入值。
        /// </summary>
        public ushort LastWriteValue => LastWrite.Value;

        /// <summary>
        /// 写入历史。
        /// </summary>
        public List<(ushort Address, ushort Value)> Writes { get; } = new();

        /// <summary>
        /// 写入异常注入。
        /// </summary>
        public Exception? ThrowOnWrite { get; set; }

        /// <summary>
        /// 建立连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>完成任务。</returns>
        public ValueTask ConnectAsync(CancellationToken cancellationToken = default) {
            IsConnected = true;
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// 断开连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>完成任务。</returns>
        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default) {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// 读取保持寄存器。
        /// </summary>
        /// <param name="address">寄存器地址。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>寄存器值。</returns>
        public ValueTask<ushort> ReadHoldingRegisterAsync(ushort address, CancellationToken cancellationToken = default) {
            _readValues.TryGetValue(address, out var value);
            return ValueTask.FromResult(value);
        }

        /// <summary>
        /// 写单寄存器。
        /// </summary>
        /// <param name="address">寄存器地址。</param>
        /// <param name="value">写入值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>完成任务。</returns>
        public ValueTask WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken cancellationToken = default) {
            if (ThrowOnWrite is not null) {
                throw ThrowOnWrite;
            }

            Writes.Add((address, value));
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// 设置读取值。
        /// </summary>
        /// <param name="address">寄存器地址。</param>
        /// <param name="value">寄存器值。</param>
        public void SetReadValue(ushort address, ushort value) {
            _readValues[address] = value;
        }

        /// <summary>
        /// 释放资源。
        /// </summary>
        /// <returns>完成任务。</returns>
        public ValueTask DisposeAsync() {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }
}
