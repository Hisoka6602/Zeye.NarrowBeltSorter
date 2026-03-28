namespace Zeye.NarrowBeltSorter.Core.Manager.Chutes {

    /// <summary>
    /// 智嵌 32 路继电器客户端适配器接口（协议无关）。
    /// </summary>
    public interface IZhiQianClientAdapter : IAsyncDisposable {
        /// <summary>
        /// 获取客户端是否已连接。
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 连接到智嵌设备。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        ValueTask ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 断开与智嵌设备的连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        ValueTask DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 读取全部 DO 通道状态。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>按通道顺序返回的 DO 状态列表。</returns>
        ValueTask<IReadOnlyList<bool>> ReadDoStatesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 写入单路 DO 状态。
        /// </summary>
        /// <param name="doIndex">DO 通道编号。</param>
        /// <param name="isOn">目标状态。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        ValueTask WriteSingleDoAsync(int doIndex, bool isOn, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量写入多路 DO 状态。
        /// </summary>
        /// <param name="doStates">待写入的通道与状态映射。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        ValueTask WriteBatchDoAsync(IReadOnlyDictionary<int, bool> doStates, CancellationToken cancellationToken = default);

        /// <summary>
        /// 发送红外驱动器下发帧到智嵌设备。
        /// </summary>
        /// <param name="frame">红外驱动器帧字节。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        ValueTask WriteInfraredFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default);
    }
}
