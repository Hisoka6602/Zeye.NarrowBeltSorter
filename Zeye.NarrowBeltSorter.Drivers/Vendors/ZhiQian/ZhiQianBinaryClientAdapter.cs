using NLog;
using Polly;
using Polly.Retry;
using System.Text;
using System.Threading.Channels;
using TouchSocket.Core;
using TouchSocket.Sockets;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Utilities.Chutes;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.ZhiQian {

    /// <summary>
    /// 智嵌 32 路继电器 TCP 客户端适配器（TouchSocket + Polly）。
    /// 写操作使用自定义二进制协议（手册第 7.1.1 节）：
    ///   单路写：0x70 命令，10 字节帧；
    ///   批量写：0x57 命令，15 字节帧，含校验和。
    /// 读操作使用 ASCII 协议（手册第 7.2.5 节）：zq {addr} get y qz。
    /// </summary>
    public sealed class ZhiQianBinaryClientAdapter : IZhiQianClientAdapter {
        private static readonly Logger Log = LogManager.GetLogger(nameof(ZhiQianBinaryClientAdapter));

        /// <summary>批量写命令（0x57）帧头首字节。</summary>
        private const byte FrameHeader0 = 0x48;
        /// <summary>批量写命令（0x57）帧头次字节。</summary>
        private const byte FrameHeader1 = 0x3A;
        /// <summary>帧尾首字节（'E'）。</summary>
        private const byte FrameTail0 = 0x45;
        /// <summary>帧尾次字节（'D'）。</summary>
        private const byte FrameTail1 = 0x44;
        /// <summary>批量写命令码（0x57），对应 15 字节帧。</summary>
        private const byte CmdBatchWrite = 0x57;
        /// <summary>单路写命令码（0x70），对应 10 字节帧。</summary>
        private const byte CmdSingleWrite = 0x70;

        private readonly byte _deviceAddress;
        private readonly int _commandTimeoutMs;
        private readonly AsyncRetryPolicy _writeRetryPolicy;
        private readonly AsyncRetryPolicy _readRetryPolicy;
        private readonly Action<TouchSocketConfig> _configureAction;
        // _connectionGate：保护连接/断开操作并发；_requestGate：保证"一问一答"串行（含写防并发）。
        private readonly SemaphoreSlim _connectionGate = new(1, 1);
        private readonly SemaphoreSlim _requestGate = new(1, 1);

        // 接收缓冲：将 TouchSocket 事件驱动 → Channel 异步可等待桥接（仅用于 ASCII 读应答）。
        private readonly Channel<string> _responseChannel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        private readonly StringBuilder _recvBuffer = new();
        private readonly object _recvLock = new();

        private TcpClient? _client;
        private bool _configured;
        private bool _disposed;

        /// <summary>
        /// 初始化智嵌二进制 TCP 客户端适配器（TCP 连接）。
        /// </summary>
        /// <param name="host">设备 IP 地址。</param>
        /// <param name="port">设备端口（1~65535，手册默认 1030）。</param>
        /// <param name="deviceAddress">设备地址（0~255，手册默认 1）。</param>
        /// <param name="commandTimeoutMs">单命令超时（毫秒，最小 100）。</param>
        /// <param name="retryCount">重试次数（不含首次）。</param>
        /// <param name="retryDelayMs">重试间隔（毫秒）。</param>
        public ZhiQianBinaryClientAdapter(
            string host,
            int port,
            byte deviceAddress,
            int commandTimeoutMs,
            int retryCount,
            int retryDelayMs)
            : this(
                deviceAddress,
                commandTimeoutMs,
                retryCount,
                retryDelayMs,
                config => config.SetRemoteIPHost(new IPHost($"{host}:{port}"))) {
        }

        /// <summary>
        /// 初始化智嵌二进制 TCP 客户端适配器（内部工厂构造，接受任意配置动作）。
        /// </summary>
        private ZhiQianBinaryClientAdapter(
            byte deviceAddress,
            int commandTimeoutMs,
            int retryCount,
            int retryDelayMs,
            Action<TouchSocketConfig> configureAction) {
            // 步骤1：校验关键参数边界。
            if (deviceAddress > 255) {
                throw new ArgumentOutOfRangeException(nameof(deviceAddress), "协议站号必须在 0~255 范围。");
            }

            if (commandTimeoutMs < 100) {
                throw new ArgumentOutOfRangeException(nameof(commandTimeoutMs), "单命令超时最小值为 100ms。");
            }

            if (retryCount < 0) {
                throw new ArgumentOutOfRangeException(nameof(retryCount), "重试次数不能小于 0。");
            }

            if (retryDelayMs < 10) {
                throw new ArgumentOutOfRangeException(nameof(retryDelayMs), "重试间隔最小值为 10ms。");
            }

            // 步骤2：初始化字段与重试策略（写策略略多于读策略）。
            _deviceAddress = deviceAddress;
            _commandTimeoutMs = commandTimeoutMs;
            _configureAction = configureAction;
            _writeRetryPolicy = Policy
                .Handle<Exception>(ex => ex is not OperationCanceledException)
                .WaitAndRetryAsync(
                    retryCount,
                    attempt => TimeSpan.FromMilliseconds(retryDelayMs * attempt),
                    (ex, _, attempt, _) => Log.Warn(ex, "ZhiQian写重试 addr={0} attempt={1} ex={2}", _deviceAddress, attempt, ex.Message));
            _readRetryPolicy = Policy
                .Handle<Exception>(ex => ex is not OperationCanceledException)
                .WaitAndRetryAsync(
                    Math.Max(0, retryCount - 1),
                    attempt => TimeSpan.FromMilliseconds(retryDelayMs * attempt),
                    onRetryAsync: async (ex, _, attempt, _) => {
                        Log.Warn(ex, "ZhiQian读重试 addr={0} attempt={1} ex={2}", _deviceAddress, attempt, ex.Message);
                        // 超时时必须重连以清空 TCP 缓冲区，防止设备延迟应答污染下次重试（幽灵请求防护）。
                        if (ex is TimeoutException) {
                            await ReconnectForReadRetryAsync().ConfigureAwait(false);
                        }
                    });
        }

        /// <inheritdoc />
        public bool IsConnected => _client?.Online ?? false;

        /// <inheritdoc />
        public async ValueTask ConnectAsync(CancellationToken cancellationToken = default) {
            // 步骤1：等待门控，防止并发重入。
            // 步骤2：首次创建客户端实例并注册接收插件；已连接时直接返回。
            // 步骤3：执行 TCP 连接，记录日志。
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                ThrowIfDisposed();
                if (IsConnected) {
                    return;
                }

                if (!_configured) {
                    var cfg = new TouchSocketConfig();
                    _configureAction(cfg);
                    cfg.ConfigurePlugins(plugins => {
                        plugins.AddTcpReceivedPlugin(OnTcpReceived);
                    });

                    _client = new TcpClient();
                    await _client.SetupAsync(cfg).ConfigureAwait(false);
                    _configured = true;
                }

                var opId = OperationIdFactory.CreateShortOperationId();
                Log.Info("ZhiQian连接开始 opId={0} addr={1}", opId, _deviceAddress);
                await _client!.ConnectAsync(cancellationToken).ConfigureAwait(false);
                Log.Info("ZhiQian连接完成 opId={0} addr={1}", opId, _deviceAddress);
            }
            finally {
                _connectionGate.Release();
            }
        }

        /// <inheritdoc />
        public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default) {
            // 步骤1：等待门控，防止并发重入。
            // 步骤2：关闭 TCP 链路并记录日志。
            cancellationToken.ThrowIfCancellationRequested();
            await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                if (!IsConnected) {
                    return;
                }

                await _client!.CloseAsync("主动断开", cancellationToken).ConfigureAwait(false);
                Log.Info("ZhiQian已断开 addr={0}", _deviceAddress);
            }
            finally {
                _connectionGate.Release();
            }
        }

        /// <inheritdoc />
        public async ValueTask<IReadOnlyList<bool>> ReadDoStatesAsync(CancellationToken cancellationToken = default) {
            // 步骤1：校验连接状态。
            // 步骤2：发送 ASCII "get y" 指令（手册 7.2.5 节）并等待应答。
            // 步骤3：解析应答报文 "zq {addr} ret y:{v1} {v2} ... {v32} qz" 中的 32 路状态。
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfNotConnected();
            bool[]? result = null;
            await _readRetryPolicy.ExecuteAsync(async ct => {
                var cmd = BuildGetYAsciiCommand();
                var response = await SendAsciiAndReceiveAsync(cmd, ct).ConfigureAwait(false);
                result = ParseGetYResponse(response);
            }, cancellationToken).ConfigureAwait(false);
            return result ?? Array.Empty<bool>();
        }

        /// <inheritdoc />
        public async ValueTask WriteSingleDoAsync(int doIndex, bool isOn, CancellationToken cancellationToken = default) {
            // 步骤1：校验 Y 路范围。
            // 步骤2：构造二进制 0x70 命令帧（手册 7.1.1.2 节），发送后无需等待应答。
            cancellationToken.ThrowIfCancellationRequested();
            ZhiQianAddressMap.ValidateDoIndex(doIndex);
            ThrowIfNotConnected();
            await _writeRetryPolicy.ExecuteAsync(async ct => {
                var frame = BuildSingleWriteFrame(doIndex, isOn);
                await SendBinaryAsync(frame, ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async ValueTask WriteBatchDoAsync(IReadOnlyDictionary<int, bool> doStates, CancellationToken cancellationToken = default) {
            // 步骤1：校验所有 Y 路范围，校验失败立即抛出，不进行部分写入。
            // 步骤2：持有 _requestGate 完成整个"回读→合并→写入"原子序列，
            //        防止在回读与写入之间其他指令插入造成状态混乱（串行通道要求）。
            // 步骤3：构造二进制 0x57 命令帧（手册 7.1.1.1 节），一次写入 32 路，发送后无需等待应答。
            cancellationToken.ThrowIfCancellationRequested();
            if (doStates.Count == 0) {
                return;
            }

            foreach (var (doIndex, _) in doStates) {
                ZhiQianAddressMap.ValidateDoIndex(doIndex);
            }

            ThrowIfNotConnected();
            // 持有请求锁进行整个批量读-改-写序列，保证通道内无指令插入。
            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                await _writeRetryPolicy.ExecuteAsync(async ct => {
                    // 回读当前 32 路状态（不再通过公开 ReadDoStatesAsync，直接调用核心方法，避免重入获取 _requestGate）。
                    var cmd = BuildGetYAsciiCommand();
                    var response = await SendAsciiAndReceiveCoreAsync(cmd, ct).ConfigureAwait(false);
                    var current = ParseGetYResponse(response);
                    var target = current.ToArray();
                    foreach (var (doIndex, isOn) in doStates) {
                        target[doIndex - ZhiQianAddressMap.DoIndexMin] = isOn;
                    }

                    var frame = BuildBatchWriteFrame(target);
                    await SendBinaryCoreAsync(frame, ct).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);
            }
            finally {
                _requestGate.Release();
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            try {
                if (IsConnected) {
                    await DisconnectAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "ZhiQian释放时断开异常 addr={0}", _deviceAddress);
            }

            _client?.SafeDispose();
            _connectionGate.Dispose();
            _requestGate.Dispose();
        }

        /// <summary>
        /// TouchSocket 接收插件回调：将接收到的字节追加到缓冲区（ASCII），凑齐完整帧后写入 Channel。
        /// 仅处理 ASCII 读应答（qz 帧尾），与二进制写操作不产生混淆。
        /// </summary>
        /// <param name="session">TCP 会话。</param>
        /// <param name="e">接收数据事件参数。</param>
        private Task OnTcpReceived(ITcpSession session, ReceivedDataEventArgs e) {
            // 步骤1：将新到字节转为 ASCII 文本追加到缓冲区。
            // 步骤2：扫描是否包含帧尾 "qz"；找到则提取完整帧并写入 Channel。
            var text = Encoding.ASCII.GetString(e.Memory.Span);
            lock (_recvLock) {
                _recvBuffer.Append(text);
                var s = _recvBuffer.ToString();
                var qzIdx = s.IndexOf("qz", StringComparison.Ordinal);
                if (qzIdx >= 0) {
                    var frame = s[..(qzIdx + 2)].Trim();
                    // 保留 qz 之后的残余数据，防止设备连续快速应答时后续帧丢失。
                    var remaining = s[(qzIdx + 2)..];
                    _recvBuffer.Clear();
                    if (remaining.Length > 0) {
                        _recvBuffer.Append(remaining);
                    }

                    _responseChannel.Writer.TryWrite(frame);
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 发送 ASCII 命令并等待完整应答帧（仅用于读操作）。
        /// </summary>
        /// <param name="command">ASCII 命令字符串（不含换行）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>设备应答的完整 ASCII 帧字符串。</returns>
        private async Task<string> SendAsciiAndReceiveAsync(string command, CancellationToken cancellationToken) {
            // 步骤1：获取请求锁，保证单路"一问一答"顺序。
            // 步骤2：调用核心方法执行实际发送与接收。
            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                return await SendAsciiAndReceiveCoreAsync(command, cancellationToken).ConfigureAwait(false);
            }
            finally {
                _requestGate.Release();
            }
        }

        /// <summary>
        /// 发送 ASCII 命令并等待完整应答帧的核心实现（调用方须已持有 _requestGate）。
        /// </summary>
        /// <param name="command">ASCII 命令字符串（不含换行）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>设备应答的完整 ASCII 帧字符串。</returns>
        private async Task<string> SendAsciiAndReceiveCoreAsync(string command, CancellationToken cancellationToken) {
            // 步骤1：清空 Channel 中的残留帧（避免上次异常遗留数据干扰）。
            // 步骤2：发送命令。
            // 步骤3：在超时内等待应答帧，超时则抛出 TimeoutException。
            while (_responseChannel.Reader.TryRead(out _)) { }

            var bytes = Encoding.ASCII.GetBytes(command + "\r\n");
            await _client!.SendAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_commandTimeoutMs);
            try {
                return await _responseChannel.Reader.ReadAsync(cts.Token).AsTask().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                throw new TimeoutException($"ZhiQian 读命令超时（{_commandTimeoutMs}ms），命令：{command}");
            }
        }

        /// <summary>
        /// 发送二进制帧（用于写操作，不等待应答）。
        /// </summary>
        /// <param name="frame">二进制帧数据。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task SendBinaryAsync(byte[] frame, CancellationToken cancellationToken) {
            // 步骤1：获取请求锁，防止与读操作并发，避免 TCP 流混乱。
            // 步骤2：调用核心方法执行实际发送。
            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                await SendBinaryCoreAsync(frame, cancellationToken).ConfigureAwait(false);
            }
            finally {
                _requestGate.Release();
            }
        }

        /// <summary>
        /// 发送二进制帧的核心实现（调用方须已持有 _requestGate）。
        /// </summary>
        /// <param name="frame">二进制帧数据。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private Task SendBinaryCoreAsync(byte[] frame, CancellationToken cancellationToken) =>
            _client!.SendAsync(frame.AsMemory(), cancellationToken);

        /// <summary>
        /// 读重试前的重连操作：断开并重新连接以清空 TCP 缓冲区，
        /// 防止上次超时未收到的延迟应答污染本次重试的应答帧（幽灵请求防护）。
        /// </summary>
        private async Task ReconnectForReadRetryAsync() {
            // 步骤1：断开以刷新 TCP 缓冲区（忽略断开异常，继续重连）。
            try {
                await DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex) {
                Log.Warn(ex, "ZhiQian读重试断开失败（继续重连） addr={0}", _deviceAddress);
            }

            // 步骤2：清空接收缓冲区，丢弃残余数据。
            lock (_recvLock) {
                _recvBuffer.Clear();
            }

            // 步骤3：重新连接，若失败则让上层重试策略感知异常。
            await ConnectAsync().ConfigureAwait(false);
            Log.Info("ZhiQian读重试重连完成 addr={0}", _deviceAddress);
        }

        /// <summary>
        /// 构造读取全量 DO 状态的 ASCII 命令（手册 7.2.5 节）。
        /// </summary>
        /// <returns>ASCII 命令字符串。</returns>
        private string BuildGetYAsciiCommand() => $"zq {_deviceAddress} get y qz";

        /// <summary>
        /// 构造单路写二进制帧（手册 7.1.1.2 节，0x70 命令，10 字节）。
        /// 帧格式：[0x48][0x3A][addr][0x70][relay(1-32)][state(0x01/0x00)][0x00][0x00][0x45][0x44]
        /// </summary>
        /// <param name="doIndex">Y 路编号（1~32，对应 Byte5 值 0x01~0x20）。</param>
        /// <param name="isOn">目标状态（true 闭合 / false 断开）。</param>
        /// <returns>10 字节二进制帧。</returns>
        private byte[] BuildSingleWriteFrame(int doIndex, bool isOn) => [
            FrameHeader0,
            FrameHeader1,
            _deviceAddress,
            CmdSingleWrite,
            (byte)doIndex,
            (byte)(isOn ? 0x01 : 0x00),
            0x00,  // 延时高字节（0 表示无延时）
            0x00,  // 延时低字节
            FrameTail0,
            FrameTail1
        ];

        /// <summary>
        /// 构造全量批量写二进制帧（手册 7.1.1.1 节，0x57 命令，15 字节）。
        /// 帧格式：[0x48][0x3A][addr][0x57][8字节继电器状态][校验和][0x45][0x44]
        /// 状态编码：每字节对应 4 路继电器（bits 0-3），bit0 为低路，各路按 0-based 排列。
        /// 校验和 = 前 12 字节之和的低 8 位。
        /// </summary>
        /// <param name="target">32 路目标状态数组，索引 0 对应 Y01（DoIndexMin）。</param>
        /// <returns>15 字节二进制帧。</returns>
        private byte[] BuildBatchWriteFrame(bool[] target) {
            // 步骤1：填充帧头（2字节）、设备地址（1字节）、命令码（1字节）。
            // 步骤2：编码 32 路状态到 8 字节（每字节 4 路，bit0=第1路，bit1=第2路...）。
            // 步骤3：计算校验和（前12字节之和的低8位），填充帧尾（2字节）。
            const int totalLength = 15;
            const int stateOffset = 4;
            const int stateByteCount = 8;
            const int relaysPerByte = 4;

            var frame = new byte[totalLength];
            frame[0] = FrameHeader0;
            frame[1] = FrameHeader1;
            frame[2] = _deviceAddress;
            frame[3] = CmdBatchWrite;

            // 步骤2：将 32 路状态编码为 8 字节位图（每字节4路，bits 0-3）。
            for (var i = 0; i < stateByteCount; i++) {
                byte b = 0;
                for (var bit = 0; bit < relaysPerByte; bit++) {
                    var relayIdx = i * relaysPerByte + bit;
                    if (relayIdx < target.Length && target[relayIdx]) {
                        b |= (byte)(1 << bit);
                    }
                }

                frame[stateOffset + i] = b;
            }

            // 步骤3：校验和 = 前12字节之和的低8位。
            byte checksum = 0;
            for (var i = 0; i < 12; i++) {
                checksum += frame[i];
            }

            frame[12] = checksum;
            frame[13] = FrameTail0;
            frame[14] = FrameTail1;
            return frame;
        }

        /// <summary>
        /// 解析 "get y" ASCII 应答报文，返回 32 路 DO 状态数组。
        /// 应答格式（手册 7.2.5 节）：zq {addr} ret y:{v1} {v2} ... {v32} qz
        /// </summary>
        /// <param name="response">设备应答的 ASCII 帧字符串。</param>
        /// <returns>32 元素 bool 数组，true 为闭合。</returns>
        private static bool[] ParseGetYResponse(string response) {
            // 步骤1：定位 "y:" 分隔符。
            // 步骤2：提取 "y:" 之后至 "qz" 之前的状态字符串，按空格拆分。
            // 步骤3：解析每个值（"1" 为闭合，"0" 为断开），校验数量是否满足 32 路。
            var yIdx = response.IndexOf("y:", StringComparison.Ordinal);
            if (yIdx < 0) {
                throw new InvalidDataException($"ZhiQian get y 应答格式错误，未找到 'y:' 分隔符：{response}");
            }

            var afterY = response[(yIdx + 2)..];
            var qzIdx = afterY.IndexOf("qz", StringComparison.Ordinal);
            if (qzIdx >= 0) {
                afterY = afterY[..qzIdx];
            }

            var parts = afterY.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < ZhiQianAddressMap.DoChannelCount) {
                throw new InvalidDataException($"ZhiQian get y 应答路数不足，期望 {ZhiQianAddressMap.DoChannelCount}，实际 {parts.Length}：{response}");
            }

            var result = new bool[ZhiQianAddressMap.DoChannelCount];
            for (var i = 0; i < ZhiQianAddressMap.DoChannelCount; i++) {
                result[i] = parts[i] == "1";
            }

            return result;
        }

        /// <summary>
        /// 检查实例是否已被释放，已释放时抛出 ObjectDisposedException。
        /// </summary>
        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(ZhiQianBinaryClientAdapter));
            }
        }

        /// <summary>
        /// 检查设备是否已连接，未连接时抛出 InvalidOperationException。
        /// </summary>
        private void ThrowIfNotConnected() {
            if (!IsConnected) {
                throw new InvalidOperationException("智嵌设备未连接，请先调用 ConnectAsync。");
            }
        }
    }
}
