using Polly;
using Polly.Retry;
using TouchSocket.Core;
using TouchSocket.Modbus;
using TouchSocket.Sockets;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa {
    /// <summary>
    /// 雷码 Modbus 客户端适配器（TouchSocket TCP + TouchSocket.Modbus + Polly）。
    /// </summary>
    public sealed class LeiMaModbusClientAdapter : ILeiMaModbusClientAdapter {
        private readonly byte _slaveAddress;
        private readonly int _modbusTimeoutMilliseconds;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly object _syncRoot = new();
        private readonly string _remoteHost;

        private ModbusTcpMaster? _master;
        private bool _configured;
        private bool _disposed;

        /// <summary>
        /// 初始化雷码 Modbus 客户端适配器。
        /// </summary>
        /// <param name="remoteHost">目标 TCP 地址，格式示例：127.0.0.1:502。</param>
        /// <param name="slaveAddress">从站地址（1~247）。</param>
        /// <param name="modbusTimeoutMilliseconds">Modbus 请求超时（毫秒）。</param>
        /// <param name="retryCount">重试次数（不含首次）。</param>
        public LeiMaModbusClientAdapter(
            string remoteHost,
            byte slaveAddress,
            int modbusTimeoutMilliseconds = 1000,
            int retryCount = 2)
            : this(remoteHost, new ModbusTcpMaster(), slaveAddress, modbusTimeoutMilliseconds, retryCount) {
        }

        /// <summary>
        /// 初始化雷码 Modbus 客户端适配器（用于测试注入）。
        /// </summary>
        /// <param name="remoteHost">目标 TCP 地址。</param>
        /// <param name="master">Modbus 主站实例。</param>
        /// <param name="slaveAddress">从站地址（1~247）。</param>
        /// <param name="modbusTimeoutMilliseconds">Modbus 请求超时（毫秒）。</param>
        /// <param name="retryCount">重试次数（不含首次）。</param>
        internal LeiMaModbusClientAdapter(
            string remoteHost,
            ModbusTcpMaster master,
            byte slaveAddress,
            int modbusTimeoutMilliseconds = 1000,
            int retryCount = 2) {
            if (string.IsNullOrWhiteSpace(remoteHost)) {
                throw new ArgumentException("目标 TCP 地址不能为空。", nameof(remoteHost));
            }

            _remoteHost = remoteHost;
            _master = master ?? throw new ArgumentNullException(nameof(master));
            if (slaveAddress is 0 or > 247) {
                throw new ArgumentOutOfRangeException(nameof(slaveAddress), "从站地址必须在 1~247 范围。");
            }

            if (modbusTimeoutMilliseconds <= 0) {
                throw new ArgumentOutOfRangeException(nameof(modbusTimeoutMilliseconds), "请求超时必须大于 0。");
            }

            if (retryCount < 0) {
                throw new ArgumentOutOfRangeException(nameof(retryCount), "重试次数不能小于 0。");
            }

            _slaveAddress = slaveAddress;
            _modbusTimeoutMilliseconds = modbusTimeoutMilliseconds;
            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount,
                    attempt => TimeSpan.FromMilliseconds(50 * attempt));
        }

        /// <inheritdoc />
        public bool IsConnected {
            get {
                lock (_syncRoot) {
                    return _master?.Online == true;
                }
            }
        }

        /// <inheritdoc />
        public async ValueTask ConnectAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            ModbusTcpMaster? masterToConnect;
            var needSetup = false;
            lock (_syncRoot) {
                if (_master?.Online == true) {
                    return;
                }

                masterToConnect = _master;
                needSetup = !_configured;
            }

            if (masterToConnect is null) {
                throw new InvalidOperationException("Modbus 主站未初始化。");
            }

            if (needSetup) {
                // 步骤1：首次连接前异步配置远端地址，避免同步阻塞异步初始化。
                // 步骤2：配置完成后标记，后续连接复用已配置主站对象。
                var config = new TouchSocketConfig();
                config.SetRemoteIPHost(new IPHost(_remoteHost));
                await masterToConnect.SetupAsync(config).ConfigureAwait(false);
                lock (_syncRoot) {
                    _configured = true;
                }
            }

            await masterToConnect!.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            ModbusTcpMaster? masterToClose;
            lock (_syncRoot) {
                masterToClose = _master;
            }

            if (masterToClose is null) {
                return;
            }

            if (masterToClose.Online) {
                await masterToClose.CloseAsync("主动断开", cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async ValueTask<ushort> ReadHoldingRegisterAsync(ushort address, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            var master = GetConnectedMaster();

            // 步骤1：使用 Polly 重试策略封装 Modbus FC3 读取。
            // 步骤2：校验响应成功且长度满足单寄存器。
            // 步骤3：按大端解析单寄存器值。
            var response = await _retryPolicy.ExecuteAsync(async ct => {
                    var readResponse = await master
                        .ReadHoldingRegistersAsync(_slaveAddress, address, 1, _modbusTimeoutMilliseconds, ct)
                        .ConfigureAwait(false);
                    if (!readResponse.IsSuccess) {
                        throw new InvalidOperationException($"读取寄存器失败，错误码：{readResponse.ErrorCode}。");
                    }

                    return readResponse;
                },
                cancellationToken).ConfigureAwait(false);

            var data = response.Data;
            if (data.Length < 2) {
                throw new InvalidDataException("读取寄存器响应长度不足。");
            }

            return (ushort)((data.Span[0] << 8) | data.Span[1]);
        }

        /// <inheritdoc />
        public async ValueTask WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            var master = GetConnectedMaster();

            // 步骤1：使用 Polly 重试策略封装 Modbus FC6 写入。
            // 步骤2：校验写入响应成功，失败即抛出异常。
            _ = await _retryPolicy.ExecuteAsync(async ct => {
                    var writeResponse = await master
                        .WriteSingleRegisterAsync(_slaveAddress, address, value, _modbusTimeoutMilliseconds, ct)
                        .ConfigureAwait(false);
                    if (!writeResponse.IsSuccess) {
                        throw new InvalidOperationException($"写入寄存器失败，错误码：{writeResponse.ErrorCode}。");
                    }

                    return writeResponse;
                },
                cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            ModbusTcpMaster? masterToDispose;
            lock (_syncRoot) {
                masterToDispose = _master;
                _master = null;
                _configured = false;
            }

            if (masterToDispose is null) {
                return;
            }

            if (masterToDispose.Online) {
                await masterToDispose.CloseAsync("释放断开", CancellationToken.None).ConfigureAwait(false);
            }

            masterToDispose.Dispose();
        }

        /// <summary>
        /// 获取已连接的 Modbus 主站。
        /// </summary>
        /// <returns>已连接主站对象。</returns>
        private ModbusTcpMaster GetConnectedMaster() {
            ThrowIfDisposed();

            lock (_syncRoot) {
                if (_master is null || !_master.Online) {
                    throw new InvalidOperationException("TCP 链路未连接。");
                }

                return _master;
            }
        }

        /// <summary>
        /// 对已释放对象进行调用检查。
        /// </summary>
        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(LeiMaModbusClientAdapter));
            }
        }
    }
}
