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
                    async (ex, _, _, _) => {
                        Log.Warn(ex, "ZhiQian读超时，执行重连后重试 addr={0}", _address);
                        await ReconnectForReadRetryAsync().ConfigureAwait(false);
                    });
        }

        public bool IsConnected => _client?.Online == true;

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
                                var text = e.Memory.Span.ToString(Encoding.ASCII);
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

        public async ValueTask<IReadOnlyList<bool>> ReadDoStatesAsync(CancellationToken cancellationToken = default) {
            return await _readRetryPolicy.ExecuteAsync(async () => {
                await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try {
                    EnsureConnected();
                    var command = $"zq {_address} get y qz";
                    await SendAsciiAsync(command, cancellationToken).ConfigureAwait(false);
                    var response = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                    return ParseReadResponse(response);
                }
                finally {
                    _requestGate.Release();
                }
            }).ConfigureAwait(false);
        }

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

        private async Task<bool[]> ReadDoStatesCoreAsync(CancellationToken cancellationToken) {
            EnsureConnected();
            await SendAsciiAsync($"zq {_address} get y qz", cancellationToken).ConfigureAwait(false);
            var response = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            return ParseReadResponse(response);
        }

        private async Task SendAsciiAsync(string command, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            var data = Encoding.ASCII.GetBytes(command + "\r\n");
            await _client!.SendAsync(data).ConfigureAwait(false);
        }

        private async Task<string> ReadFrameAsync(CancellationToken cancellationToken) {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_commandTimeoutMs);
            try {
                return await _readResponses.Reader.ReadAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                throw new TimeoutException($"读取智嵌返回超时（{_commandTimeoutMs}ms）。");
            }
        }

        private static bool[] ParseReadResponse(string response) {
            var states = new bool[ZhiQianAddressMap.DoChannelCount];
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

        private void AppendReceivedText(string text) {
            if (string.IsNullOrEmpty(text)) {
                return;
            }

            lock (_receiveBuffer) {
                _receiveBuffer.Append(text);
                while (true) {
                    var snapshot = _receiveBuffer.ToString();
                    var tailIndex = snapshot.IndexOf("qz", StringComparison.OrdinalIgnoreCase);
                    if (tailIndex < 0) {
                        break;
                    }

                    var frame = snapshot[..(tailIndex + 2)].Trim();
                    _receiveBuffer.Clear();
                    _receiveBuffer.Append(snapshot[(tailIndex + 2)..]);
                    _readResponses.Writer.TryWrite(frame);
                }
            }
        }

        private void EnsureConnected() {
            ThrowIfDisposed();
            if (_client is null || !_client.Online) {
                throw new InvalidOperationException("连接尚未建立。");
            }
        }

        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(ZhiQianBinaryClientAdapter));
            }
        }
    }
}
