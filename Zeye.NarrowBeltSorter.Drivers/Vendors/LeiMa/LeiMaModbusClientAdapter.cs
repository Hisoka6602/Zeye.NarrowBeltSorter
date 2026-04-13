using NLog;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using System.IO.Ports;
using TouchSocket.Core;
using TouchSocket.Modbus;
using TouchSocket.Sockets;
using System.Collections.Concurrent;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Utilities.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa {

    /// <summary>
    /// 雷码 Modbus 客户端适配器（TouchSocket + TouchSocket.Modbus + Polly）。
    /// </summary>
    public sealed class LeiMaModbusClientAdapter : ILeiMaModbusClientAdapter {
        private static readonly Logger DebugLogger = LogManager.GetLogger(nameof(LeiMaModbusClientAdapter));
        private const string SerialRtuLinkDisconnectedMessage = "串口 RTU 链路未连接。";
        private const string TcpLinkDisconnectedMessage = "TCP 链路未连接。";
        private const string LinkDisconnectedFlagKey = "IsLinkDisconnected";
        private readonly byte _slaveAddress;
        private readonly int _modbusTimeoutMilliseconds;
        private readonly IAsyncPolicy _requestPolicy;
        private readonly object _syncRoot = new();
        private readonly bool _isSerialRtu;
        private readonly string? _remoteHost;
        private readonly Action<TouchSocketConfig>? _configureAction;
        private readonly LeiMaSerialRtuSharedConnection? _serialSharedConnection;
        private readonly SemaphoreSlim _operationGate = new(1, 1);

        private IModbusMaster? _master;
        private ModbusTcpMaster? _tcpMaster;
        private ModbusRtuMaster? _rtuMaster;
        private bool _configured;
        private bool _disposed;
        private static readonly ConcurrentDictionary<string, LeiMaSerialRtuSharedConnection> SerialRtuConnections = new();

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
                false,
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
                GetOrCreateSerialRtuConnection(
                    portName,
                    baudRate,
                    parity,
                    dataBits,
                    stopBits),
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
                false,
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
                true,
                master,
                configureAction,
                slaveAddress,
                modbusTimeoutMilliseconds,
                retryCount,
                null) {
        }

        private LeiMaModbusClientAdapter(
            LeiMaSerialRtuSharedConnection serialSharedConnection,
            Action<TouchSocketConfig> configureAction,
            byte slaveAddress,
            int modbusTimeoutMilliseconds,
            int retryCount,
            string? remoteHost)
            : this(
                true,
                serialSharedConnection.Master,
                configureAction,
                slaveAddress,
                modbusTimeoutMilliseconds,
                retryCount,
                remoteHost) {
            _serialSharedConnection = serialSharedConnection;
        }

        /// <summary>
        /// 初始化雷码 Modbus 客户端适配器（通用）。
        /// </summary>
        /// <param name="isSerialRtu">是否为串口 RTU 模式。</param>
        /// <param name="master">Modbus 主站实例。</param>
        /// <param name="configureAction">主站配置动作。</param>
        /// <param name="slaveAddress">从站地址（1~247）。</param>
        /// <param name="modbusTimeoutMilliseconds">Modbus 请求超时（毫秒）。</param>
        /// <param name="retryCount">重试次数（不含首次）。</param>
        /// <param name="remoteHost">TCP 远端地址。</param>
        private LeiMaModbusClientAdapter(
            bool isSerialRtu,
            IModbusMaster master,
            Action<TouchSocketConfig>? configureAction,
            byte slaveAddress,
            int modbusTimeoutMilliseconds,
            int retryCount,
            string? remoteHost) {
            // 步骤1：按传输模式校验关键输入，避免创建后才暴露配置错误。
            if (!isSerialRtu && string.IsNullOrWhiteSpace(remoteHost)) {
                throw new ArgumentException("目标 TCP 地址不能为空。", nameof(remoteHost));
            }

            if (isSerialRtu && configureAction is null) {
                throw new ArgumentNullException(nameof(configureAction), "串口 RTU 模式必须提供串口配置动作。");
            }

            // 步骤2：初始化主站与基础参数，并进行边界校验。
            _isSerialRtu = isSerialRtu;
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
            _remoteHost = remoteHost;
            _modbusTimeoutMilliseconds = modbusTimeoutMilliseconds;
            var retryPolicy = Policy
                .Handle<Exception>(ex => ex is not OperationCanceledException)
                  .WaitAndRetryAsync(
                    retryCount,
                    attempt => TimeSpan.FromMilliseconds(50 * attempt),
                    (exception, _, retryAttempt, _) => {
                        DebugLogger.Info(exception, "Modbus重试 stage=LeiMaModbusClientAdapter.Retry transport={0} slaveId={1} retryAttempt={2} elapsedMs={3} exceptionType={4} exceptionMessage={5} result=Retrying", GetTransportName(), _slaveAddress, retryAttempt, 0, exception.GetType().Name, exception.Message);
                    });
            var timeoutPolicy = Policy.TimeoutAsync(TimeSpan.FromMilliseconds(_modbusTimeoutMilliseconds + 200), TimeoutStrategy.Optimistic);
            _requestPolicy = Policy.WrapAsync(retryPolicy, timeoutPolicy);
        }

        /// <inheritdoc />
        public bool IsConnected {
            get {
                lock (_syncRoot) {
                    return (_isSerialRtu ? _rtuMaster?.Online : _tcpMaster?.Online) == true;
                }
            }
        }

        /// <inheritdoc />
        public async ValueTask ConnectAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            var connectOperationId = CreateOperationId();
            if (_isSerialRtu && _serialSharedConnection is not null) {
                await ConnectSerialRtuSharedAsync(connectOperationId, cancellationToken).ConfigureAwait(false);
                return;
            }
            var needSetup = false;
            ModbusTcpMaster? tcpMaster;
            ModbusRtuMaster? rtuMaster;
            Action<TouchSocketConfig>? configureAction;
            lock (_syncRoot) {
                ThrowIfDisposed();
                tcpMaster = _tcpMaster;
                rtuMaster = _rtuMaster;
                if (tcpMaster is null && rtuMaster is null) {
                    throw new InvalidOperationException("Modbus 主站未初始化。");
                }

                if ((_isSerialRtu ? rtuMaster?.Online : tcpMaster?.Online) == true) {
                    return;
                }

                needSetup = !_configured;
                configureAction = _configureAction;
            }

            if (needSetup) {
                lock (_syncRoot) {
                    ThrowIfDisposed();
                    if ((_isSerialRtu ? _rtuMaster?.Online : _tcpMaster?.Online) == true) {
                        return;
                    }

                    needSetup = !_configured;
                }

                if (!needSetup) {
                    return;
                }

                // 步骤1：首次连接前异步配置链路参数，避免同步阻塞异步初始化。
                // 步骤2：配置完成后标记，后续连接复用已配置主站对象。
                var config = new TouchSocketConfig();
                configureAction?.Invoke(config);
                if (_isSerialRtu) {
                    if (rtuMaster is null) {
                        throw new InvalidOperationException("串口 RTU 主站未初始化。");
                    }

                    await rtuMaster.SetupAsync(config).ConfigureAwait(false);
                }
                else {
                    if (tcpMaster is null) {
                        throw new InvalidOperationException("TCP 主站未初始化。");
                    }

                    await tcpMaster.SetupAsync(config).ConfigureAwait(false);
                }

                lock (_syncRoot) {
                    ThrowIfDisposed();
                    _configured = true;
                }
            }

            lock (_syncRoot) {
                ThrowIfDisposed();
                if ((_isSerialRtu ? _rtuMaster?.Online : _tcpMaster?.Online) == true) {
                    return;
                }

                tcpMaster = _tcpMaster;
                rtuMaster = _rtuMaster;
                if (tcpMaster is null && rtuMaster is null) {
                    throw new InvalidOperationException("Modbus 主站未初始化。");
                }
            }

            try {
                if (_isSerialRtu) {
                    if (rtuMaster is null) {
                        throw new InvalidOperationException("串口 RTU 主站未初始化。");
                    }

                    await rtuMaster.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    _ = await TrySendAlarmResetProbeAsync(rtuMaster, connectOperationId, cancellationToken).ConfigureAwait(false);
                }
                else {
                    if (tcpMaster is null) {
                        throw new InvalidOperationException("TCP 主站未初始化。");
                    }

                    await tcpMaster.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    _ = await TrySendAlarmResetProbeAsync(tcpMaster, connectOperationId, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) {
                if (_isSerialRtu) {
                    DebugLogger.Error(
                        ex,
                        "Modbus串口连接失败 operationId={0} stage=LeiMaModbusClientAdapter.Connect transport={1} slaveId={2} com={3} result=Failed",
                        connectOperationId,
                        GetTransportName(),
                        _slaveAddress,
                        _serialSharedConnection?.PortName ?? string.Empty);
                }
                else {
                    DebugLogger.Error(
                        ex,
                        "Modbus连接失败 operationId={0} stage=LeiMaModbusClientAdapter.Connect transport={1} slaveId={2} endpoint={3} result=Failed",
                        connectOperationId,
                        GetTransportName(),
                        _slaveAddress,
                        _remoteHost ?? string.Empty);
                }

                throw;
            }

            DebugLogger.Info("Modbus连接完成 operationId={0} stage=LeiMaModbusClientAdapter.Connect transport={1} slaveId={2} register={3} retryAttempt={4} elapsedMs={5} exceptionType={6} exceptionMessage={7} result=Connected", connectOperationId, GetTransportName(), _slaveAddress, 0, 1, 0, "None", "None");
        }

        /// <summary>
        /// 建立串口 RTU 共享连接并完成连通性探测。
        /// </summary>
        /// <param name="connectOperationId">连接操作编号。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private async ValueTask ConnectSerialRtuSharedAsync(string connectOperationId, CancellationToken cancellationToken) {
            var shared = _serialSharedConnection ?? throw new InvalidOperationException("串口共享连接上下文未初始化。");
            await shared.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                try {
                    ThrowIfDisposed();
                    var serialMaster = shared.Master;
                    if (!shared.Configured) {
                        var config = new TouchSocketConfig();
                        _configureAction?.Invoke(config);
                        await serialMaster.SetupAsync(config).ConfigureAwait(false);
                        shared.Configured = true;
                    }

                    if (!serialMaster.Online) {
                        await serialMaster.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    }
                    _ = await TrySendAlarmResetProbeAsync(serialMaster, connectOperationId, cancellationToken).ConfigureAwait(false);
                    lock (_syncRoot) {
                        ThrowIfDisposed();
                        _configured = true;
                    }
                    DebugLogger.Info("Modbus连接完成 operationId={0} stage=LeiMaModbusClientAdapter.Connect transport={1} slaveId={2} register={3} retryAttempt={4} elapsedMs={5} exceptionType={6} exceptionMessage={7} result=Connected", connectOperationId, GetTransportName(), _slaveAddress, 0, 1, 0, "None", "None");
                }
                catch (Exception ex) {
                    DebugLogger.Error(
                        ex,
                        "Modbus串口连接失败 operationId={0} stage=LeiMaModbusClientAdapter.Connect transport={1} slaveId={2} com={3} result=Failed",
                        connectOperationId,
                        GetTransportName(),
                        _slaveAddress,
                        shared.PortName);
                    throw;
                }
            }
            finally {
                shared.Gate.Release();
            }
        }

        /// <summary>
        /// 发送告警复位探测命令，验证链路可读写性。
        /// </summary>
        /// <param name="master">Modbus 主站。</param>
        /// <param name="connectOperationId">连接操作编号。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>探测成功返回 true，否则返回 false。</returns>
        private async Task<bool> TrySendAlarmResetProbeAsync(IModbusMaster master, string connectOperationId, CancellationToken cancellationToken) {
            // 步骤1：通过统一重试策略发送复位探测命令，验证链路可写能力。
            // 步骤2：校验写入响应成功且数据长度有效，确保链路返回符合协议预期。
            // 步骤3：捕获异常并记录告警日志，避免探测失败影响主连接流程。
            try {
                _ = await _requestPolicy.ExecuteAsync(async ct => {
                    var response = await master
                        .WriteSingleRegisterAsync(
                            _slaveAddress,
                            LeiMaRegisters.Command,
                            LeiMaRegisters.CommandAlarmReset,
                            _modbusTimeoutMilliseconds,
                            ct)
                        .ConfigureAwait(false);
                    if (!response.IsSuccess) {
                        throw new InvalidOperationException($"连接探测失败，错误码：{response.ErrorCode}。");
                    }

                    if (response.Data.Length < 2) {
                        throw new InvalidDataException("连接探测响应长度不足。");
                    }

                    return response;
                }, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) {
                DebugLogger.Log(NLog.LogLevel.Warn, ex, "Modbus连接复位探测失败 operationId={0} stage=LeiMaModbusClientAdapter.ConnectResetProbe transport={1} slaveId={2} register={3} exceptionType={4} exceptionMessage={5} result=Failed",
                   connectOperationId,
                    GetTransportName(),
                    _slaveAddress,
                    LeiMaRegisters.Command,
                    ex.GetType().Name,
                    ex.Message);

                return false;
            }
        }

        /// <inheritdoc />
        public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default) {
            // 步骤1：在锁内捕获主站引用并校验当前对象状态，避免并发释放导致空引用。
            // 步骤2：根据在线状态决定是否需要执行断开，离线场景直接返回。
            // 步骤3：按传输模式调用对应主站 CloseAsync，异常由上层统一感知处理。
            cancellationToken.ThrowIfCancellationRequested();
            if (_isSerialRtu && _serialSharedConnection is not null) {
                await _serialSharedConnection.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try {
                    ThrowIfDisposed();
                    if (_serialSharedConnection.Master.Online) {
                        await _serialSharedConnection.Master.CloseAsync("主动断开", cancellationToken).ConfigureAwait(false);
                    }
                }
                finally {
                    _serialSharedConnection.Gate.Release();
                }

                return;
            }
            ModbusTcpMaster? tcpMaster;
            ModbusRtuMaster? rtuMaster;
            var isOnline = false;
            lock (_syncRoot) {
                ThrowIfDisposed();
                tcpMaster = _tcpMaster;
                rtuMaster = _rtuMaster;
                if (tcpMaster is null && rtuMaster is null) {
                    throw new InvalidOperationException("Modbus 主站未初始化。");
                }

                isOnline = (_isSerialRtu ? rtuMaster?.Online : tcpMaster?.Online) == true;
            }

            if (!isOnline) {
                return;
            }

            if (_isSerialRtu) {
                if (rtuMaster is null) {
                    throw new InvalidOperationException("串口 RTU 主站未初始化。");
                }

                await rtuMaster.CloseAsync("主动断开", cancellationToken).ConfigureAwait(false);
            }
            else {
                if (tcpMaster is null) {
                    throw new InvalidOperationException("TCP 主站未初始化。");
                }

                await tcpMaster.CloseAsync("主动断开", cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async ValueTask<ushort> ReadHoldingRegisterAsync(ushort address, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteSerializedAsync(
                async token => {
                    if (_isSerialRtu && _serialSharedConnection is not null) {
                        await _serialSharedConnection.Gate.WaitAsync(token).ConfigureAwait(false);
                        try {
                            return await ReadHoldingRegisterCoreAsync(address, token).ConfigureAwait(false);
                        }
                        finally {
                            _serialSharedConnection.Gate.Release();
                        }
                    }
                    return await ReadHoldingRegisterCoreAsync(address, token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 执行单寄存器读取核心流程。
        /// </summary>
        /// <param name="address">寄存器地址。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>寄存器值。</returns>
        private async ValueTask<ushort> ReadHoldingRegisterCoreAsync(ushort address, CancellationToken cancellationToken) {
            var master = GetConnectedMaster();
            var readOperationId = CreateOperationId();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var retryAttempt = 0;
            DebugLogger.Info(
                "Modbus读取开始 operationId={0} stage=LeiMaModbusClientAdapter.ReadHoldingRegisterAsync transport={1} slaveId={2} register={3}",
                readOperationId,
                GetTransportName(),
                _slaveAddress,
                address);
            try {
                // 步骤1：使用 Polly 重试策略封装 Modbus FC3 读取。
                // 步骤2：校验响应成功且长度满足单寄存器。
                // 步骤3：按大端解析单寄存器值。
                var response = await _requestPolicy.ExecuteAsync(async ct => {
                    retryAttempt++;
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

                var result = (ushort)((data.Span[0] << 8) | data.Span[1]);
                watch.Stop();
                DebugLogger.Info("Modbus读取 operationId={0} stage=LeiMaModbusClientAdapter.ReadHoldingRegisterAsync transport={1} slaveId={2} register={3} retryAttempt={4} elapsedMs={5} exceptionType={6} exceptionMessage={7} result=Success", readOperationId, GetTransportName(), _slaveAddress, address, retryAttempt, watch.ElapsedMilliseconds, "None", "None");
                return result;
            }
            catch (Exception ex) {
                watch.Stop();
                DebugLogger.Log(NLog.LogLevel.Warn, ex, "Modbus读取失败 operationId={0} stage=LeiMaModbusClientAdapter.ReadHoldingRegisterAsync transport={1} slaveId={2} register={3} retryAttempt={4} elapsedMs={5} exceptionType={6} exceptionMessage={7} result=Failed", readOperationId, GetTransportName(), _slaveAddress, address, retryAttempt, watch.ElapsedMilliseconds, ex.GetType().Name, ex.Message);
                throw;
            }
        }

        /// <inheritdoc />
        public async ValueTask WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            await ExecuteSerializedAsync(
                async token => {
                    if (_isSerialRtu && _serialSharedConnection is not null) {
                        await _serialSharedConnection.Gate.WaitAsync(token).ConfigureAwait(false);
                        try {
                            await WriteSingleRegisterCoreAsync(address, value, token).ConfigureAwait(false);
                        }
                        finally {
                            _serialSharedConnection.Gate.Release();
                        }

                        return;
                    }

                    await WriteSingleRegisterCoreAsync(address, value, token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 执行单寄存器写入核心流程。
        /// </summary>
        /// <param name="address">寄存器地址。</param>
        /// <param name="value">写入值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private async ValueTask WriteSingleRegisterCoreAsync(ushort address, ushort value, CancellationToken cancellationToken) {
            var master = GetConnectedMaster();
            var writeOperationId = CreateOperationId();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var retryAttempt = 0;
            try {
                // 步骤1：使用 Polly 重试策略封装 Modbus FC6 写入。
                // 步骤2：校验写入响应成功，失败即抛出异常。
                _ = await _requestPolicy.ExecuteAsync(async ct => {
                    retryAttempt++;
                    var writeResponse = await master
                        .WriteSingleRegisterAsync(_slaveAddress, address, value, _modbusTimeoutMilliseconds, ct)
                        .ConfigureAwait(false);
                    if (!writeResponse.IsSuccess) {
                        throw new InvalidOperationException($"写入寄存器失败，错误码：{writeResponse.ErrorCode}。");
                    }

                    return writeResponse;
                },
                    cancellationToken).ConfigureAwait(false);
                watch.Stop();
                DebugLogger.Info("Modbus写入 operationId={0} stage=LeiMaModbusClientAdapter.WriteSingleRegisterAsync transport={1} slaveId={2} register={3} retryAttempt={4} elapsedMs={5} exceptionType={6} exceptionMessage={7} result=Success", writeOperationId, GetTransportName(), _slaveAddress, address, retryAttempt, watch.ElapsedMilliseconds, "None", "None");
            }
            catch (Exception ex) {
                watch.Stop();
                DebugLogger.Log(NLog.LogLevel.Warn, ex, "Modbus写入失败 operationId={0} stage=LeiMaModbusClientAdapter.WriteSingleRegisterAsync transport={1} slaveId={2} register={3} retryAttempt={4} elapsedMs={5} exceptionType={6} exceptionMessage={7} result=Failed", writeOperationId, GetTransportName(), _slaveAddress, address, retryAttempt, watch.ElapsedMilliseconds, ex.GetType().Name, ex.Message);
                throw;
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync() {
            // 步骤1：处理重复释放场景，保障多次调用幂等。
            // 步骤2：串口 RTU 模式下先归还共享连接引用，再在锁内清理当前实例状态。
            // 步骤3：独占连接模式下按在线状态关闭链路并释放主站资源。
            if (_disposed) {
                return;
            }

            _disposed = true;
            if (_isSerialRtu && _serialSharedConnection is not null) {
                await _serialSharedConnection.Gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try {
                    ReleaseSerialRtuConnection(_serialSharedConnection.Key);
                }
                finally {
                    _serialSharedConnection.Gate.Release();
                }

                lock (_syncRoot) {
                    _master = null;
                    _tcpMaster = null;
                    _rtuMaster = null;
                    _configured = false;
                }
                _operationGate.Dispose();
                return;
            }

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

            if (_isSerialRtu && rtuMaster?.Online == true) {
                await rtuMaster.CloseAsync("释放断开", CancellationToken.None).ConfigureAwait(false);
            }
            else if (!_isSerialRtu && tcpMaster?.Online == true) {
                await tcpMaster.CloseAsync("释放断开", CancellationToken.None).ConfigureAwait(false);
            }

            if (masterToDispose is IDisposable disposable) {
                disposable.Dispose();
            }
            _operationGate.Dispose();
        }

        /// <summary>
        /// 获取已连接的 Modbus 主站。
        /// </summary>
        /// <returns>已连接主站对象。</returns>
        private IModbusMaster GetConnectedMaster() {
            lock (_syncRoot) {
                ThrowIfDisposed();
                if (_master is null) {
                    throw new InvalidOperationException("Modbus 主站未初始化。");
                }

                var isOnline = (_isSerialRtu ? _rtuMaster?.Online : _tcpMaster?.Online) == true;
                if (!isOnline) {
                    var disconnectedException = CreateLinkDisconnectedException();
                    throw disconnectedException;
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

        /// <summary>
        /// 创建短格式操作编号。
        /// </summary>
        /// <returns>操作编号。</returns>
        private static string CreateOperationId() =>
            OperationIdFactory.CreateShortOperationId();

        /// <summary>
        /// 获取传输模式名称。
        /// </summary>
        /// <returns>传输模式名称。</returns>
        private string GetTransportName() {
            return _isSerialRtu ? "SerialRtu" : "TcpGateway";
        }

        /// <summary>
        /// 获取或创建串口 RTU 共享连接并增加引用计数。
        /// </summary>
        /// <param name="portName">串口名称。</param>
        /// <param name="baudRate">波特率。</param>
        /// <param name="parity">校验位。</param>
        /// <param name="dataBits">数据位。</param>
        /// <param name="stopBits">停止位。</param>
        /// <returns>共享连接实例。</returns>
        private static LeiMaSerialRtuSharedConnection GetOrCreateSerialRtuConnection(
          string portName,
          int baudRate,
          Parity parity,
          int dataBits,
          StopBits stopBits) {
            var connectionKey = BuildSerialConnectionKey(portName, baudRate, parity, dataBits, stopBits);
            var sharedConnection = SerialRtuConnections.AddOrUpdate(
                connectionKey,
                _ => new LeiMaSerialRtuSharedConnection(connectionKey, portName, new ModbusRtuMaster()),
                (_, existing) => existing);
            lock (sharedConnection.SyncRoot) {
                checked {
                    sharedConnection.RefCount++;
                }
            }

            return sharedConnection;
        }

        /// <summary>
        /// 释放串口 RTU 共享连接引用并在引用归零时销毁连接资源。
        /// </summary>
        /// <param name="connectionKey">共享连接键。</param>
        private static void ReleaseSerialRtuConnection(string connectionKey) {
            if (!SerialRtuConnections.TryGetValue(connectionKey, out var sharedConnection)) {
                return;
            }

            var shouldDispose = false;
            lock (sharedConnection.SyncRoot) {
                if (sharedConnection.RefCount > 0) {
                    sharedConnection.RefCount--;
                }

                if (sharedConnection.RefCount == 0) {
                    shouldDispose = true;
                }
            }

            if (!shouldDispose) {
                return;
            }

            if (SerialRtuConnections.TryRemove(connectionKey, out var removed)) {
                if (removed.Master.Online) {
                    try {
                        removed.Master.CloseAsync("释放断开", CancellationToken.None).GetAwaiter().GetResult();
                    }
                    catch (Exception ex) {
                        DebugLogger.Log(NLog.LogLevel.Warn, ex, "串口 RTU 共享连接关闭异常（资源仍将释放）key={0}", connectionKey);
                    }
                }

                removed.Master.Dispose();
            }
        }

        /// <summary>
        /// 构建串口 RTU 共享连接键。
        /// </summary>
        /// <param name="portName">串口名称。</param>
        /// <param name="baudRate">波特率。</param>
        /// <param name="parity">校验位。</param>
        /// <param name="dataBits">数据位。</param>
        /// <param name="stopBits">停止位。</param>
        /// <returns>连接键字符串。</returns>
        private static string BuildSerialConnectionKey(
            string portName,
            int baudRate,
            Parity parity,
            int dataBits,
            StopBits stopBits) {
            return $"{portName.Trim().ToUpperInvariant()}|{baudRate}|{parity}|{dataBits}|{stopBits}";
        }

        /// <summary>
        /// 在实例级串行门控下执行返回值异步操作。
        /// </summary>
        /// <typeparam name="T">返回值类型。</typeparam>
        /// <param name="operation">目标操作。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>操作结果。</returns>
        private async ValueTask<T> ExecuteSerializedAsync<T>(
            Func<CancellationToken, ValueTask<T>> operation,
            CancellationToken cancellationToken) {
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                try {
                    return await operation(cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex) when (IsLinkDisconnectedException(ex)) {
                    try {
                        _ = await _requestPolicy.ExecuteAsync(
                            async token => {
                                await ConnectAsync(token).ConfigureAwait(false);
                                return true;
                            },
                            cancellationToken).ConfigureAwait(false);
                        return await operation(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception retryEx) {
                        DebugLogger.Log(
                            NLog.LogLevel.Warn,
                            retryEx,
                            "链路断开后重连重试失败 stage=LeiMaModbusClientAdapter.ExecuteSerializedAsync transport={0} slaveId={1}",
                            GetTransportName(),
                            _slaveAddress);
                        throw;
                    }
                }
            }
            finally {
                _operationGate.Release();
            }
        }

        /// <summary>
        /// 在实例级串行门控下执行无返回值异步操作。
        /// </summary>
        /// <param name="operation">目标操作。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private async ValueTask ExecuteSerializedAsync(
            Func<CancellationToken, ValueTask> operation,
            CancellationToken cancellationToken) {
            await ExecuteSerializedAsync(
                async token => {
                    await operation(token).ConfigureAwait(false);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 创建链路未连接异常并附加标记。
        /// </summary>
        /// <returns>链路未连接异常。</returns>
        private InvalidOperationException CreateLinkDisconnectedException() {
            var exception = new InvalidOperationException(_isSerialRtu ? SerialRtuLinkDisconnectedMessage : TcpLinkDisconnectedMessage);
            exception.Data[LinkDisconnectedFlagKey] = true;
            return exception;
        }

        /// <summary>
        /// 判断异常是否为链路离线异常。
        /// </summary>
        /// <param name="exception">待判断异常。</param>
        /// <returns>是链路离线异常返回 true，否则返回 false。</returns>
        private static bool IsLinkDisconnectedException(InvalidOperationException exception) {
            return exception.Data.Contains(LinkDisconnectedFlagKey)
                && exception.Data[LinkDisconnectedFlagKey] is bool disconnected
                && disconnected;
        }
    }
}
