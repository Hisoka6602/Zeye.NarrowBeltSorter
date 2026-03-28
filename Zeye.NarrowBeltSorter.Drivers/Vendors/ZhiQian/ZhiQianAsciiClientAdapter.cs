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
    /// 智嵌 32 路继电器 ASCII TCP 客户端适配器（TouchSocket + Polly）。
    /// 通过普通 TCP 连接使用 ASCII 协议（手册第 7.2 节）控制继电器，
    /// 无需 Modbus 协议层开销，实现读写 DO 最小能力接口。
    /// </summary>
    public sealed class ZhiQianAsciiClientAdapter : IZhiQianClientAdapter {
        private static readonly Logger Log = LogManager.GetLogger(nameof(ZhiQianAsciiClientAdapter));

        private readonly byte _deviceAddress;
        private readonly int _commandTimeoutMs;
        private readonly AsyncRetryPolicy _writeRetryPolicy;
        private readonly AsyncRetryPolicy _readRetryPolicy;
        private readonly Action<TouchSocketConfig> _configureAction;
        // _connectionGate：保护连接/断开操作并发；_requestGate：保证"一问一答"串行。
        private readonly SemaphoreSlim _connectionGate = new(1, 1);
        private readonly SemaphoreSlim _requestGate = new(1, 1);

        // 接收缓冲：将 TouchSocket 事件驱动 → Channel 异步可等待桥接。
        private readonly Channel<string> _responseChannel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        private readonly StringBuilder _recvBuffer = new();
        private readonly object _recvLock = new();

        private TcpClient? _client;
        private bool _configured;
        private bool _disposed;

        /// <summary>
        /// 初始化智嵌 ASCII TCP 客户端适配器（TCP 连接）。
        /// </summary>
        /// <param name="host">设备 IP 地址。</param>
        /// <param name="port">设备端口（1~65535，手册默认 1030）。</param>
        /// <param name="deviceAddress">设备地址（ASCII 协议站号 0~255）。</param>
        /// <param name="commandTimeoutMs">单命令超时（毫秒）。</param>
        /// <param name="retryCount">重试次数（不含首次）。</param>
        /// <param name="retryDelayMs">重试间隔（毫秒）。</param>
        public ZhiQianAsciiClientAdapter(
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
        /// 初始化智嵌 ASCII TCP 客户端适配器（内部工厂构造，接受任意配置动作）。
        /// </summary>
        private ZhiQianAsciiClientAdapter(
            byte deviceAddress,
            int commandTimeoutMs,
            int retryCount,
            int retryDelayMs,
            Action<TouchSocketConfig> configureAction) {
            // 步骤1：校验关键参数边界。
            if (deviceAddress > 255) {
                throw new ArgumentOutOfRangeException(nameof(deviceAddress), "ASCII 协议站号必须在 0~255 范围。");
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
                    (ex, _, attempt, _) => Log.Warn(ex, "ZhiQian读重试 addr={0} attempt={1} ex={2}", _deviceAddress, attempt, ex.Message));
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
                var cmd = BuildGetYCommand();
                var response = await SendAndReceiveAsync(cmd, ct).ConfigureAwait(false);
                result = ParseGetYResponse(response);
            }, cancellationToken).ConfigureAwait(false);
            return result ?? Array.Empty<bool>();
        }

        /// <inheritdoc />
        public async ValueTask WriteSingleDoAsync(int doIndex, bool isOn, CancellationToken cancellationToken = default) {
            // 步骤1：校验 Y 路范围。
            // 步骤2：发送 ASCII "set yXX state" 指令（手册 7.2.2 节）并等待应答确认。
            cancellationToken.ThrowIfCancellationRequested();
            ZhiQianAddressMap.ValidateDoIndex(doIndex);
            ThrowIfNotConnected();
            await _writeRetryPolicy.ExecuteAsync(async ct => {
                var cmd = BuildSetSingleCommand(doIndex, isOn);
                var response = await SendAndReceiveAsync(cmd, ct).ConfigureAwait(false);
                VerifySetResponse(response);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async ValueTask WriteBatchDoAsync(IReadOnlyDictionary<int, bool> doStates, CancellationToken cancellationToken = default) {
            // 步骤1：校验所有 Y 路范围，校验失败立即抛出，不进行部分写入。
            // 步骤2：先回读当前 32 路状态作为基准，合并目标变更。
            // 步骤3：发送 ASCII 全量 set 指令（手册 7.2.1 节），一次写入 32 路，减少往返次数。
            cancellationToken.ThrowIfCancellationRequested();
            if (doStates.Count == 0) {
                return;
            }

            foreach (var (doIndex, _) in doStates) {
                ZhiQianAddressMap.ValidateDoIndex(doIndex);
            }

            ThrowIfNotConnected();
            await _writeRetryPolicy.ExecuteAsync(async ct => {
                // 回读当前状态以获取 32 路基准。
                var current = await ReadDoStatesAsync(ct).ConfigureAwait(false);
                var target = current.ToArray();
                foreach (var (doIndex, isOn) in doStates) {
                    target[doIndex - ZhiQianAddressMap.DoIndexMin] = isOn;
                }

                var cmd = BuildSetBatchCommand(target);
                var response = await SendAndReceiveAsync(cmd, ct).ConfigureAwait(false);
                VerifySetResponse(response);
            }, cancellationToken).ConfigureAwait(false);
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
        /// TouchSocket 接收插件回调：将接收到的字节追加到缓冲区，凑齐完整帧后写入 Channel。
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
        /// 发送 ASCII 命令并等待完整应答帧。
        /// </summary>
        /// <param name="command">ASCII 命令字符串（不含换行）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>设备应答的完整 ASCII 帧字符串。</returns>
        private async Task<string> SendAndReceiveAsync(string command, CancellationToken cancellationToken) {
            // 步骤1：获取请求锁，保证单路"一问一答"顺序。
            // 步骤2：清空 Channel 中的残留帧（避免上次异常遗留数据干扰）。
            // 步骤3：发送命令。
            // 步骤4：在超时内等待应答帧，超时则抛出 TimeoutException。
            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                // 清空残留帧，避免读到上次异常遗留数据。
                while (_responseChannel.Reader.TryRead(out _)) { }

                var bytes = Encoding.ASCII.GetBytes(command + "\r\n");
                await _client!.SendAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(_commandTimeoutMs);
                try {
                    return await _responseChannel.Reader.ReadAsync(cts.Token).AsTask().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                    throw new TimeoutException($"ZhiQian 命令超时（{_commandTimeoutMs}ms），命令：{command}");
                }
            }
            finally {
                _requestGate.Release();
            }
        }

        /// <summary>
        /// 构造读取全量 DO 状态的 ASCII 命令（手册 7.2.5 节）。
        /// </summary>
        /// <returns>ASCII 命令字符串。</returns>
        private string BuildGetYCommand() => $"zq {_deviceAddress} get y qz";

        /// <summary>
        /// 构造写单路 DO 的 ASCII 命令（手册 7.2.2 节）。
        /// </summary>
        /// <param name="doIndex">Y 路编号（1~32）。</param>
        /// <param name="isOn">目标状态（true 闭合 / false 断开）。</param>
        /// <returns>ASCII 命令字符串。</returns>
        private string BuildSetSingleCommand(int doIndex, bool isOn) =>
            $"zq {_deviceAddress} set y{doIndex:D2} {(isOn ? "1" : "0")} qz";

        /// <summary>
        /// 构造写全量 32 路 DO 的 ASCII 命令（手册 7.2.1 节）。
        /// </summary>
        /// <param name="target">32 路目标状态数组，索引 0 对应 Y01。</param>
        /// <returns>ASCII 命令字符串。</returns>
        private string BuildSetBatchCommand(bool[] target) {
            var states = string.Join(' ', target.Select(on => on ? "1" : "0"));
            return $"zq {_deviceAddress} set {states} qz";
        }

        /// <summary>
        /// 解析 "get y" 应答报文，返回 32 路 DO 状态数组。
        /// 应答格式（手册 7.2.5 节）：zq {addr} ret y:{v1} {v2} ... {v32} qz
        /// </summary>
        /// <param name="response">设备应答的 ASCII 帧字符串。</param>
        /// <returns>32 元素 bool 数组，true 为闭合。</returns>
        private bool[] ParseGetYResponse(string response) {
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
        /// 校验 set 系列命令的应答报文中是否包含 "ret"。
        /// </summary>
        /// <param name="response">设备应答的 ASCII 帧字符串。</param>
        private static void VerifySetResponse(string response) {
            if (!response.Contains("ret", StringComparison.Ordinal)) {
                throw new InvalidOperationException($"ZhiQian set 应答未包含 'ret'，应答内容：{response}");
            }
        }

        /// <summary>
        /// 检查实例是否已被释放，已释放时抛出 ObjectDisposedException。
        /// </summary>
        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(ZhiQianAsciiClientAdapter));
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
