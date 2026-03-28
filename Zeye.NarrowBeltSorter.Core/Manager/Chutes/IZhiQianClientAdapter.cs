namespace Zeye.NarrowBeltSorter.Core.Manager.Chutes {

    /// <summary>
    /// 智嵌 32 路继电器客户端适配器接口（协议无关）。
    /// </summary>
    public interface IZhiQianClientAdapter : IAsyncDisposable {
        bool IsConnected { get; }

        ValueTask ConnectAsync(CancellationToken cancellationToken = default);

        ValueTask DisconnectAsync(CancellationToken cancellationToken = default);

        ValueTask<IReadOnlyList<bool>> ReadDoStatesAsync(CancellationToken cancellationToken = default);

        ValueTask WriteSingleDoAsync(int doIndex, bool isOn, CancellationToken cancellationToken = default);

        ValueTask WriteBatchDoAsync(IReadOnlyDictionary<int, bool> doStates, CancellationToken cancellationToken = default);

        /// <summary>
        /// 发送红外驱动器下发帧到智嵌设备。
        /// </summary>
        /// <param name="frame">红外驱动器帧字节。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        ValueTask WriteInfraredFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default);
    }
}
