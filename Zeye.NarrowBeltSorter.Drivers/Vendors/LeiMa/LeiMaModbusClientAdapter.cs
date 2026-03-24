namespace Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa {
    /// <summary>
    /// 雷玛 Modbus 客户端适配器（占位实现，供上层依赖注入替换）。
    /// </summary>
    public sealed class LeiMaModbusClientAdapter : ILeiMaModbusClientAdapter {
        private bool _connected;

        /// <inheritdoc />
        public bool IsConnected => _connected;

        /// <inheritdoc />
        public ValueTask ConnectAsync(CancellationToken cancellationToken = default) {
            _connected = true;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default) {
            _connected = false;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask<ushort> ReadHoldingRegisterAsync(ushort address, CancellationToken cancellationToken = default) {
            _ = address;
            throw new NotSupportedException("LeiMaModbusClientAdapter 需要由实际 Modbus 实现替换。");
        }

        /// <inheritdoc />
        public ValueTask WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken cancellationToken = default) {
            _ = address;
            _ = value;
            throw new NotSupportedException("LeiMaModbusClientAdapter 需要由实际 Modbus 实现替换。");
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() {
            _connected = false;
            return ValueTask.CompletedTask;
        }
    }
}
