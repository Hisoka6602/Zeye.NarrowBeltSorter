namespace Zeye.NarrowBeltSorter.Core.Manager.TrackSegment {
    /// <summary>
    /// 雷玛 Modbus 客户端抽象。
    /// </summary>
    public interface ILeiMaModbusClientAdapter : IAsyncDisposable {
        /// <summary>
        /// 当前链路是否已连接。
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 建立连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        ValueTask ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 断开连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        ValueTask DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 读取单个保持寄存器。
        /// </summary>
        /// <param name="address">寄存器地址。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>寄存器值。</returns>
        ValueTask<ushort> ReadHoldingRegisterAsync(ushort address, CancellationToken cancellationToken = default);

        /// <summary>
        /// 写入单个保持寄存器。
        /// </summary>
        /// <param name="address">寄存器地址。</param>
        /// <param name="value">写入值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        ValueTask WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken cancellationToken = default);
    }
}
