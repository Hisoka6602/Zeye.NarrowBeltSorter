namespace Zeye.NarrowBeltSorter.Core.Manager.Chutes {

    /// <summary>
    /// 智嵌 32 路继电器客户端适配器接口。
    /// 抽象读写 DO 最小能力，使 ZhiQianChuteManager 不直接依赖底层协议细节。
    /// </summary>
    public interface IZhiQianClientAdapter : IAsyncDisposable {

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
        /// 读取 32 路 DO 继电器状态。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>长度固定为 32 的状态数组，索引 0 对应 Y01，true 为闭合。</returns>
        ValueTask<IReadOnlyList<bool>> ReadDoStatesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 写单路 DO 继电器状态。
        /// </summary>
        /// <param name="doIndex">Y 路编号（1~32）。</param>
        /// <param name="isOn">目标状态（true 为闭合，false 为断开）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        ValueTask WriteSingleDoAsync(int doIndex, bool isOn, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量写 DO 继电器状态（一次写多路，减少往返次数）。
        /// </summary>
        /// <param name="doStates">键：Y 路编号（1~32）；值：目标状态。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        ValueTask WriteBatchDoAsync(IReadOnlyDictionary<int, bool> doStates, CancellationToken cancellationToken = default);
    }
}
