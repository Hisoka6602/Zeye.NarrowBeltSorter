using Polly;
using Polly.Retry;
using System.IO.Ports;
using TouchSocket.Core;
using TouchSocket.Modbus;
using TouchSocket.Sockets;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa {
    /// <summary>
    /// 雷码 Modbus 客户端适配器（TouchSocket + TouchSocket.Modbus + Polly）。
    /// </summary>
    public sealed class LeiMaModbusClientAdapter : ILeiMaModbusClientAdapter {
        private readonly byte _slaveAddress;
        private readonly int _modbusTimeoutMilliseconds;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly object _syncRoot = new();
        private readonly ModbusTransportMode _transportMode;
        private readonly Action<TouchSocketConfig>? _configureAction;

        private IModbusMaster? _master;
        private ModbusTcpMaster? _tcpMaster;
        private ModbusRtuMaster? _rtuMaster;
        private bool _configured;
        private bool _disposed;

        /// <summary>
        /// 初始化雷码 Modbus TCP 客户端适配器。
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
            : this(
                ModbusTransportMode.TcpGateway,
                new ModbusTcpMaster(),
                config => config.SetRemoteIPHost(new IPHost(remoteHost)),
                slaveAddress,
                modbusTimeoutMilliseconds,
                retryCount,
                remoteHost) {
        }

        /// <summary>
        /// 初始化雷码 Modbus 串口 RTU 客户端适配器。
        /// </summary>
        /// <param name="portName">串口名称。</param>
        /// <param name="baudRate">波特率。</param>
        /// <param name="parity">校验位。</param>
        /// <param name="dataBits">数据位。</param>
        /// <param name="stopBits">停止位。</param>
        /// <param name="slaveAddress">从站地址（1~247）。</param>
        /// <param name="modbusTimeoutMilliseconds">Modbus 请求超时（毫秒）。</param>
        /// <param name="retryCount">重试次数（不含首次）。</param>
        public LeiMaModbusClientAdapter(
            string portName,
            int baudRate,
            Parity parity,
            int dataBits,
            StopBits stopBits,
            byte slaveAddress,
            int modbusTimeoutMilliseconds = 1000,
            int retryCount = 2)
            : this(
                ModbusTransportMode.SerialRtu,
                new ModbusRtuMaster(),
                config => config.SetSerialPortOption(option => {
                    option.PortName = portName;
                    option.BaudRate = baudRate;
                    option.Parity = parity;
                    option.DataBits = dataBits;
                    option.StopBits = stopBits;
                }),
                slaveAddress,
                modbusTimeoutMilliseconds,
                retryCount,
                null) {
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
            int retryCount = 2)
            : this(
                ModbusTransportMode.TcpGateway,
                master,
                config => config.SetRemoteIPHost(new IPHost(remoteHost)),
                slaveAddress,
                modbusTimeoutMilliseconds,
                retryCount,
                remoteHost) {
        }

        /// <summary>
        /// 初始化雷码 Modbus 客户端适配器（测试注入：串口 RTU）。
        /// </summary>
        /// <param name="master">Modbus RTU 主站实例。</param>
        /// <param name="configureAction">主站配置动作。</param>
        /// <param name="slaveAddress">从站地址（1~247）。</param>
        /// <param name="modbusTimeoutMilliseconds">Modbus 请求超时（毫秒）。</param>
        /// <param name="retryCount">重试次数（不含首次）。</param>
        internal LeiMaModbusClientAdapter(
            ModbusRtuMaster master,
            Action<TouchSocketConfig> configureAction,
            byte slaveAddress,
            int modbusTimeoutMilliseconds = 1000,
            int retryCount = 2)
            : this(
                ModbusTransportMode.SerialRtu,
                master,
                configureAction,
                slaveAddress,
                modbusTimeoutMilliseconds,
                retryCount,
                null) {
        }

        /// <summary>
        /// 初始化雷码 Modbus 客户端适配器（通用）。
        /// </summary>
        /// <param name="transportMode">传输模式。</param>
        /// <param name="master">Modbus 主站实例。</param>
        /// <param name="configureAction">主站配置动作。</param>
        /// <param name="slaveAddress">从站地址（1~247）。</param>
        /// <param name="modbusTimeoutMilliseconds">Modbus 请求超时（毫秒）。</param>
        /// <param name="retryCount">重试次数（不含首次）。</param>
        /// <param name="remoteHost">TCP 远端地址。</param>
        private LeiMaModbusClientAdapter(
            ModbusTransportMode transportMode,
            IModbusMaster master,
            Action<TouchSocketConfig>? configureAction,
            byte slaveAddress,
            int modbusTimeoutMilliseconds,
            int retryCount,
            string? remoteHost) {
            // 步骤1：按传输模式校验关键输入，避免创建后才暴露配置错误。
            if (transportMode == ModbusTransportMode.TcpGateway && string.IsNullOrWhiteSpace(remoteHost)) {
                throw new ArgumentException("目标 TCP 地址不能为空。", nameof(remoteHost));
            }

            if (transportMode == ModbusTransportMode.SerialRtu && configureAction is null) {
                throw new ArgumentNullException(nameof(configureAction), "串口 RTU 模式必须提供串口配置动作。");
            }

            // 步骤2：初始化主站与基础参数，并进行边界校验。
            _transportMode = transportMode;
            _master = master ?? throw new ArgumentNullException(nameof(master));
            _configureAction = configureAction;
            if (master is ModbusTcpMaster tcpMaster) {
                _tcpMaster = tcpMaster;
            }
            else if (master is ModbusRtuMaster rtuMaster) {
                _rtuMaster = rtuMaster;
            }
            else {
                throw new ArgumentException("仅支持 ModbusTcpMaster 或 ModbusRtuMaster。", nameof(master));
            }

            if (slaveAddress is 0 or > 247) {
                throw new ArgumentOutOfRangeException(nameof(slaveAddress), "从站地址必须在 1~247 范围。");
            }

            if (modbusTimeoutMilliseconds <= 0) {
                throw new ArgumentOutOfRangeException(nameof(modbusTimeoutMilliseconds), "请求超时必须大于 0。");
            }

            if (retryCount < 0) {
                throw new ArgumentOutOfRangeException(nameof(retryCount), "重试次数不能小于 0。");
            }

            // 步骤3：构建统一重试策略，复用到 FC3/FC6 调用路径。
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
                    return IsOnlineUnsafe();
                }
            }
        }

        /// <inheritdoc />
        public async ValueTask ConnectAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            var needSetup = false;
            lock (_syncRoot) {
                if (IsOnlineUnsafe()) {
                    return;
                }

                needSetup = !_configured;
            }

            if (needSetup) {
                // 步骤1：首次连接前异步配置链路参数，避免同步阻塞异步初始化。
                // 步骤2：配置完成后标记，后续连接复用已配置主站对象。
                var config = new TouchSocketConfig();
                _configureAction?.Invoke(config);
                await SetupAsyncUnsafe(config).ConfigureAwait(false);
                lock (_syncRoot) {
                    _configured = true;
                }
            }

            await ConnectUnsafeAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            IModbusMaster? masterToClose;
            lock (_syncRoot) {
                masterToClose = _master;
            }

            if (masterToClose is null) {
                return;
            }

            if (IsOnlineUnsafe()) {
                await CloseUnsafeAsync("主动断开", cancellationToken).ConfigureAwait(false);
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
            IModbusMaster? masterToDispose;
            ModbusTcpMaster? tcpMaster;
            ModbusRtuMaster? rtuMaster;
            lock (_syncRoot) {
                masterToDispose = _master;
                tcpMaster = _tcpMaster;
                rtuMaster = _rtuMaster;
                _master = null;
                _tcpMaster = null;
                _rtuMaster = null;
                _configured = false;
            }

            if (masterToDispose is null) {
                return;
            }

            if (_transportMode == ModbusTransportMode.SerialRtu && rtuMaster?.Online == true) {
                await rtuMaster.CloseAsync("释放断开", CancellationToken.None).ConfigureAwait(false);
            }
            else if (_transportMode == ModbusTransportMode.TcpGateway && tcpMaster?.Online == true) {
                await tcpMaster.CloseAsync("释放断开", CancellationToken.None).ConfigureAwait(false);
            }

            if (masterToDispose is IDisposable disposable) {
                disposable.Dispose();
            }
        }

        /// <summary>
        /// 获取已连接的 Modbus 主站。
        /// </summary>
        /// <returns>已连接主站对象。</returns>
        private IModbusMaster GetConnectedMaster() {
            ThrowIfDisposed();

            lock (_syncRoot) {
                if (_master is null || !IsOnlineUnsafe()) {
                    throw new InvalidOperationException(_transportMode == ModbusTransportMode.SerialRtu ? "串口 RTU 链路未连接。" : "TCP 链路未连接。");
                }

                return _master;
            }
        }

        /// <summary>
        /// 在未加锁上下文判断链路在线状态。
        /// </summary>
        /// <returns>是否在线。</returns>
        private bool IsOnlineUnsafe() {
            if (_transportMode == ModbusTransportMode.SerialRtu) {
                return _rtuMaster?.Online == true;
            }

            return _tcpMaster?.Online == true;
        }

        /// <summary>
        /// 在已识别传输模式后执行 Setup。
        /// </summary>
        /// <param name="config">配置对象。</param>
        /// <returns>异步任务。</returns>
        private Task SetupAsyncUnsafe(TouchSocketConfig config) {
            if (_transportMode == ModbusTransportMode.SerialRtu) {
                return _rtuMaster?.SetupAsync(config) ?? Task.CompletedTask;
            }

            return _tcpMaster?.SetupAsync(config) ?? Task.CompletedTask;
        }

        /// <summary>
        /// 在已识别传输模式后执行连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        private Task ConnectUnsafeAsync(CancellationToken cancellationToken) {
            if (_transportMode == ModbusTransportMode.SerialRtu) {
                return _rtuMaster?.ConnectAsync(cancellationToken) ?? Task.CompletedTask;
            }

            return _tcpMaster?.ConnectAsync(cancellationToken) ?? Task.CompletedTask;
        }

        /// <summary>
        /// 在已识别传输模式后执行断开。
        /// </summary>
        /// <param name="reason">断开原因。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        private Task CloseUnsafeAsync(string reason, CancellationToken cancellationToken) {
            if (_transportMode == ModbusTransportMode.SerialRtu) {
                return _rtuMaster?.CloseAsync(reason, cancellationToken) ?? Task.CompletedTask;
            }

            return _tcpMaster?.CloseAsync(reason, cancellationToken) ?? Task.CompletedTask;
        }

        /// <summary>
        /// 对已释放对象进行调用检查。
        /// </summary>
        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(LeiMaModbusClientAdapter));
            }
        }

        /// <summary>
        /// Modbus 传输模式。
        /// </summary>
        private enum ModbusTransportMode {
            TcpGateway = 0,
            SerialRtu = 1
        }
    }
}
