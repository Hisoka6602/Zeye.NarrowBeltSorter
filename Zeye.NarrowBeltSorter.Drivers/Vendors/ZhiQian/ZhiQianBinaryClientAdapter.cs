using NLog;
using Polly;
using Polly.Retry;
using System.Text;
using TouchSocket.Core;
using TouchSocket.Sockets;
using System.Threading.Channels;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Utilities.Chutes;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.ZhiQian {

    /// <summary>
    /// 智嵌二进制+ASCII 混合协议客户端适配器。
    /// </summary>
    public sealed class ZhiQianBinaryClientAdapter : IZhiQianClientAdapter {
        private static readonly Logger Log = LogManager.GetLogger(nameof(ZhiQianBinaryClientAdapter));

        private readonly string _host;
        private readonly int _port;
        private readonly byte _address;
        private readonly int _commandTimeoutMs;
        private readonly AsyncRetryPolicy _readRetryPolicy;
        private readonly SemaphoreSlim _requestGate = new(1, 1);
        private readonly SemaphoreSlim _connectionGate = new(1, 1);
        private readonly StringBuilder _receiveBuffer = new();
        private readonly Channel<string> _readResponses = Channel.CreateUnbounded<string>();

        private TcpClient? _client;
        private bool _configured;
        private bool _disposed;

        /// <summary>
        /// 初始化 <see cref="ZhiQianBinaryClientAdapter"/>，配置连接参数与读超时重试策略。
        /// </summary>
        /// <param name="host">设备 IP 或主机名。</param>
        /// <param name="port">TCP 端口（1~65535）。</param>
        /// <param name="deviceAddress">设备地址（1~247）。</param>
        /// <param name="commandTimeoutMs">单次命令读超时（毫秒）。</param>
        /// <param name="retryCount">读超时重试次数。</param>
        /// <param name="retryDelayMs">每次重试等待基准时间（毫秒）。</param>
        public ZhiQianBinaryClientAdapter(string host, int port, byte deviceAddress, int commandTimeoutMs, int retryCount, int retryDelayMs) {
            if (string.IsNullOrWhiteSpace(host)) {
                throw new ArgumentException("Host 不能为空。", nameof(host));
            }

            if (port is < 1 or > 65535) {
                throw new ArgumentOutOfRangeException(nameof(port), "Port 必须在 1~65535 范围。");
            }

            if (deviceAddress is 0 or > 247) {
                throw new ArgumentOutOfRangeException(nameof(deviceAddress), "DeviceAddress 必须在 1~247 范围。");
            }

            _host = host;
            _port = port;
            _address = deviceAddress;
            _commandTimeoutMs = commandTimeoutMs;
            _readRetryPolicy = Policy
                .Handle<TimeoutException>()
                .WaitAndRetryAsync(
                    retryCount,
                    i => TimeSpan.FromMilliseconds(retryDelayMs * Math.Max(i, 1)),
                    OnReadTimeoutRetryAsync);
        }

        /// <summary>获取当前是否已连接到设备。</summary>
        public bool IsConnected => _client?.Online == true;

        /// <summary>
        /// 建立到设备的 TCP 连接（幂等，已连接时直接返回）。
        /// </summary>
        public async ValueTask ConnectAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                if (IsConnected) {
                    return;
                }

                if (!_configured) {
                    var client = new TcpClient();
                    var config = new TouchSocketConfig()
                        .SetRemoteIPHost(new IPHost($"{_host}:{_port}"))
                        .ConfigurePlugins(plugins => {
                            plugins.AddTcpReceivedPlugin(async (_, e) => {
                                var text = Encoding.ASCII.GetString(e.Memory.Span);
                                AppendReceivedText(text);
                                await e.InvokeNext().ConfigureAwait(false);
                            });
                        });
                    await client.SetupAsync(config).ConfigureAwait(false);
                    _client = client;

                    _configured = true;
                }

                await _client!.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            finally {
                _connectionGate.Release();
            }
        }

        /// <summary>主动断开设备连接（未连接时直接返回）。</summary>
        public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default) {
            await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                if (_client is null || !_client.Online) {
                    return;
                }

                await _client.CloseAsync("主动断开", cancellationToken).ConfigureAwait(false);
            }
            finally {
                _connectionGate.Release();
            }
        }

        /// <summary>
        /// 读取全部 DO 通道状态（含超时重连重试）。
        /// </summary>
        public async ValueTask<IReadOnlyList<bool>> ReadDoStatesAsync(CancellationToken cancellationToken = default) {
            return await _readRetryPolicy.ExecuteAsync(async () => {
                await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try {
                    EnsureConnected();
                    var command = $"zq {_address} get y qz";
                    await SendAsciiAsync(command, cancellationToken).ConfigureAwait(false);
                    var response = await ReadFrameAsync(cancellationToken, IsReadResponseFrame).ConfigureAwait(false);
                    return ParseReadResponse(response);
                }
                finally {
                    _requestGate.Release();
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// 写单路 DO 状态，使用二进制单路协议（0x70 帧）。
        /// </summary>
        public async ValueTask WriteSingleDoAsync(int doIndex, bool isOn, CancellationToken cancellationToken = default) {
            if (!ZhiQianAddressMap.ValidateDoIndex(doIndex)) {
                throw new ArgumentOutOfRangeException(nameof(doIndex));
            }

            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                EnsureConnected();
                var frame = new byte[] { 0x48, 0x3A, _address, 0x70, (byte)doIndex, isOn ? (byte)1 : (byte)0, 0x00, 0x00, 0x45, 0x44 };
                await _client!.SendAsync(frame).ConfigureAwait(false);
            }
            finally {
                _requestGate.Release();
            }
        }

        /// <summary>
        /// 批量写 DO 状态，先读当前状态再做差异合并，最后使用二进制批量协议（0x57 帧）写入。
        /// </summary>
        public async ValueTask WriteBatchDoAsync(IReadOnlyDictionary<int, bool> doStates, CancellationToken cancellationToken = default) {
            if (doStates.Count == 0) {
                return;
            }

            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                EnsureConnected();
                var current = await ReadDoStatesCoreAsync(cancellationToken).ConfigureAwait(false);
                foreach (var (doIndex, state) in doStates) {
                    if (!ZhiQianAddressMap.ValidateDoIndex(doIndex)) {
                        throw new ArgumentOutOfRangeException(nameof(doStates), $"非法 doIndex={doIndex}");
                    }

                    current[doIndex - ZhiQianAddressMap.DoIndexMin] = state;
                }

                var payload = BuildBatchFrame(current);
                await _client!.SendAsync(payload).ConfigureAwait(false);
            }
            finally {
                _requestGate.Release();
            }
        }

        /// <summary>释放托管资源：断开连接并销毁 SemaphoreSlim。</summary>
        public async ValueTask DisposeAsync() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            await DisconnectAsync().ConfigureAwait(false);
            _client?.Dispose();
            _requestGate.Dispose();
            _connectionGate.Dispose();
        }

        /// <summary>读超时重试前的回调：记录日志并执行重连以防止幽灵请求残留。</summary>
        private async Task OnReadTimeoutRetryAsync(Exception ex, TimeSpan _, int __, Context ___) {
            Log.Warn(ex, "ZhiQian读超时，执行重连后重试 addr={0}", _address);
            await ReconnectForReadRetryAsync().ConfigureAwait(false);
        }

        /// <summary>核心读 DO 状态实现（不持 _requestGate，由调用方负责加锁）。</summary>
        private async Task<bool[]> ReadDoStatesCoreAsync(CancellationToken cancellationToken) {
            EnsureConnected();
            await SendAsciiAsync($"zq {_address} get y qz", cancellationToken).ConfigureAwait(false);
            var response = await ReadFrameAsync(cancellationToken, IsReadResponseFrame).ConfigureAwait(false);
            return ParseReadResponse(response);
        }

        /// <summary>将 ASCII 命令字符串编码并发送到设备，追加 \n 行结束符。</summary>
        private async Task SendAsciiAsync(string command, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            var data = Encoding.ASCII.GetBytes(command);
            await _client!.SendAsync(data).ConfigureAwait(false);
        }

        /// <summary>从读响应通道异步读取一帧，超时则抛出 <see cref="TimeoutException"/>。</summary>
        private async Task<string> ReadFrameAsync(CancellationToken cancellationToken, Func<string, bool>? framePredicate = null) {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_commandTimeoutMs);
            try {
                while (true) {
                    var frame = await _readResponses.Reader.ReadAsync(timeoutCts.Token).ConfigureAwait(false);
                    if (framePredicate is null || framePredicate(frame)) {
                        return frame;
                    }

                    Log.Debug("忽略非目标响应帧: {0}", frame);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                throw new TimeoutException($"读取智嵌返回超时（{_commandTimeoutMs}ms）。");
            }
        }

        /// <summary>
        /// 解析 ASCII 读响应字符串，提取各路 DO 状态。
        /// 格式为空格分隔的 token，仅处理 yNN=V 形式（y 后两位为通道号，末字符为 '1'/'0'）。
        /// </summary>
        private static bool[] ParseReadResponse(string response) {
            var states = new bool[ZhiQianAddressMap.DoChannelCount];
            var payload = ExtractYPayload(response);
            if (!string.IsNullOrWhiteSpace(payload) && payload.Length >= ZhiQianAddressMap.DoChannelCount) {
                for (var i = 0; i < ZhiQianAddressMap.DoChannelCount; i++) {
                    states[i] = payload[i] == '1';
                }

                return states;
            }

            var parts = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts) {
                if (part.Length < 3 || part[0] != 'y') {
                    continue;
                }

                if (!int.TryParse(part.Substring(1, 2), out var doIndex) || !ZhiQianAddressMap.ValidateDoIndex(doIndex)) {
                    continue;
                }

                if (part.Length >= 4) {
                    states[doIndex - ZhiQianAddressMap.DoIndexMin] = part[^1] == '1';
                }
            }

            return states;
        }

        /// <summary>
        /// 构建批量写二进制帧（0x57 协议，15 字节，含校验和）。
        /// 步骤：1. 初始化帧头 → 2. 按通道号映射字节/位 → 3. 计算累加校验和写入 frame[12]。
        /// </summary>
        private byte[] BuildBatchFrame(bool[] states) {
            var frame = new byte[15] { 0x48, 0x3A, _address, 0x57, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x45, 0x44 };
            for (var doIndex = ZhiQianAddressMap.DoIndexMin; doIndex <= ZhiQianAddressMap.DoIndexMax; doIndex++) {
                if (!states[doIndex - 1]) {
                    continue;
                }

                var channel = doIndex - 1;
                var byteOffset = channel / 4;
                var bitOffset = channel % 4;
                frame[4 + byteOffset] |= (byte)(1 << bitOffset);
            }

            var checksum = 0;
            for (var i = 0; i < 12; i++) {
                checksum = (checksum + frame[i]) & 0xFF;
            }

            frame[12] = (byte)checksum;
            return frame;
        }

        /// <summary>
        /// 读超时后的重连操作：断开现有连接、清空接收缓冲区与响应通道，再重新建立连接。
        /// </summary>
        private async Task ReconnectForReadRetryAsync() {
            await _connectionGate.WaitAsync().ConfigureAwait(false);
            try {
                if (_client is null) {
                    return;
                }

                if (_client.Online) {
                    await _client.CloseAsync("读超时重连", CancellationToken.None).ConfigureAwait(false);
                }

                lock (_receiveBuffer) {
                    _receiveBuffer.Clear();
                }

                while (_readResponses.Reader.TryRead(out _)) {
                }

                await _client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally {
                _connectionGate.Release();
            }
        }

        /// <summary>
        /// 追加设备接收到的 ASCII 文本到缓冲区，循环提取完整帧并写入读响应通道。
        /// 空白帧（或不符合格式校验的换行候选帧）不会写入通道。
        /// </summary>
        private void AppendReceivedText(string text) {
            if (string.IsNullOrEmpty(text)) {
                return;
            }

            lock (_receiveBuffer) {
                _receiveBuffer.Append(text);
                while (true) {
                    var snapshot = _receiveBuffer.ToString();
                    if (!TryExtractFrame(snapshot, out var frame, out var consumedLength)) {
                        break;
                    }

                    _receiveBuffer.Clear();
                    _receiveBuffer.Append(snapshot[consumedLength..]);
                    if (!string.IsNullOrWhiteSpace(frame)) {
                        _readResponses.Writer.TryWrite(frame);
                    }
                }
            }
        }

        /// <summary>
        /// 尝试从缓冲区快照中提取一个完整帧。
        /// 返回 <c>true</c> 表示找到并消费了一个完整分隔帧（qz 或 \n）；
        /// 返回 <c>false</c> 表示缓冲区尚不含完整帧，需等待更多数据。
        /// 当返回 <c>true</c> 且 <paramref name="frame"/> 为空时，调用方应丢弃该帧，不写入通道。
        /// </summary>
        private static bool TryExtractFrame(string snapshot, out string frame, out int consumedLength) {
            // 优先按 qz 帧尾切帧（找到 qz 即视为消费了一个完整帧）
            var qzTailIndex = snapshot.IndexOf("qz", StringComparison.OrdinalIgnoreCase);
            if (qzTailIndex >= 0) {
                consumedLength = qzTailIndex + 2;
                var candidate = snapshot[..consumedLength].Trim('\0', '\r', '\n', ' ');
                // frame 为空时调用方不写入通道，但帧本身已被消费，故统一返回 true
                frame = string.IsNullOrWhiteSpace(candidate) ? string.Empty : candidate;
                return true;
            }

            // 兜底：按换行符切帧
            var lineBreakIndex = snapshot.IndexOf('\n');
            if (lineBreakIndex >= 0) {
                consumedLength = lineBreakIndex + 1;
                var candidate = snapshot[..lineBreakIndex].Trim('\0', '\r', '\n', ' ');
                // 非空且通过格式校验才写入通道，否则 frame 置空（帧已消费，返回 true）
                frame = !string.IsNullOrWhiteSpace(candidate) && IsPossibleAsciiResponse(candidate)
                    ? candidate
                    : string.Empty;
                return true;
            }

            frame = string.Empty;
            consumedLength = 0;
            return false;
        }

        /// <summary>
        /// 判断换行候选帧是否为可能的合法 ASCII 响应，以过滤噪声。
        /// <list type="bullet">
        ///   <item><c>zq </c> 开头：查询类响应。</item>
        ///   <item>含 <c> ret </c>：带返回码响应。</item>
        ///   <item><c>y</c> 开头：DO 状态类响应，要求至少满足 yNN=V 格式（>=5 字符，y 后两位为数字，含 '='）。</item>
        /// </list>
        /// </summary>
        private static bool IsPossibleAsciiResponse(string candidate) {
            // zq 开头的为查询类 ASCII 响应
            if (candidate.StartsWith("zq ", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            // 包含 " ret " 的为带返回码的 ASCII 响应
            if (candidate.Contains(" ret ", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            // y 开头的为 DO 状态类 ASCII 响应，需要满足 yNN=V... 格式
            if (!candidate.StartsWith("y", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            // 最小长度约束：yNN=V -> 至少 5 个字符
            if (candidate.Length < 5) {
                return false;
            }

            // 校验 y 后的两位为数字（与 ParseReadResponse 中 Substring(1,2) 解析一致）
            if (!char.IsDigit(candidate[1]) || !char.IsDigit(candidate[2])) {
                return false;
            }

            // 要求包含 '='，且 '=' 位于索引 3 或之后（yNN= 中 '=' 在索引 3，为最短合法形式）
            var equalIndex = candidate.IndexOf('=');
            return equalIndex >= 3;
        }

        /// <summary>检查连接状态，未连接时抛出 <see cref="InvalidOperationException"/>。</summary>
        private void EnsureConnected() {
            ThrowIfDisposed();
            if (_client is null || !_client.Online) {
                throw new InvalidOperationException("连接尚未建立。");
            }
        }

        /// <summary>检查是否已释放，已释放时抛出 <see cref="ObjectDisposedException"/>。</summary>
        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(ZhiQianBinaryClientAdapter));
            }
        }

        private static string ExtractYPayload(string response) {
            if (string.IsNullOrWhiteSpace(response)) {
                return string.Empty;
            }

            var markerIndex = response.IndexOf("y:", StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0) {
                return string.Empty;
            }

            var start = markerIndex + 2;
            var end = start;
            while (end < response.Length && (response[end] == '0' || response[end] == '1')) {
                end++;
            }

            return end > start ? response[start..end] : string.Empty;
        }

        private static bool IsReadResponseFrame(string frame) {
            if (string.IsNullOrWhiteSpace(frame)) {
                return false;
            }

            var normalized = frame.Trim();
            return normalized.Contains(" ret ", StringComparison.OrdinalIgnoreCase)
                   && normalized.IndexOf("y:", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
