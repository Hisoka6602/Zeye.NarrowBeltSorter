namespace Zeye.NarrowBeltSorter.Core.Manager.Chutes {

    /// <summary>
    /// 智嵌 32 路继电器 Modbus 客户端适配器接口。
    /// 抽象读写线圈最小能力，使 IChuteManager 实现层不直接依赖底层协议细节。
    /// </summary>
    public interface IZhiQianModbusClientAdapter : IAsyncDisposable {

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
        /// 读取 32 路 DO 线圈状态（FC01，读线圈）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>长度固定为 32 的状态数组，索引 0 对应 Y01，true 为闭合。</returns>
        ValueTask<IReadOnlyList<bool>> ReadDoStatesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 写单路 DO（FC05，写单线圈）。
        /// </summary>
        /// <param name="doIndex">Y 路编号（1~32）。</param>
        /// <param name="isOn">目标状态（true 为闭合，false 为断开）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        ValueTask WriteSingleDoAsync(int doIndex, bool isOn, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量写 DO（FC0F，写多线圈）。
        /// 适用于同时改动多路的高性能场景，一次写入减少往返次数。
        /// </summary>
        /// <param name="doStates">键：Y 路编号（1~32）；值：目标状态。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        ValueTask WriteBatchDoAsync(IReadOnlyDictionary<int, bool> doStates, CancellationToken cancellationToken = default);
    }
}
