using NLog;
using Polly;
using Polly.Retry;
using System.IO.Ports;
using TouchSocket.Core;
using TouchSocket.Modbus;
using TouchSocket.Sockets;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Utilities.Chutes;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.ZhiQian {

    /// <summary>
    /// 智嵌 32 路继电器 Modbus 客户端适配器（TouchSocket.Modbus + Polly）。
    /// 实现读写线圈最小能力接口，供 ZhiQianChuteManager 调用。
    /// </summary>
    public sealed class ZhiQianModbusClientAdapter : IZhiQianModbusClientAdapter {
        private static readonly Logger Log = LogManager.GetLogger(nameof(ZhiQianModbusClientAdapter));

        private readonly byte _deviceAddress;
        private readonly int _commandTimeoutMs;
        private readonly AsyncRetryPolicy _writeRetryPolicy;
        private readonly AsyncRetryPolicy _readRetryPolicy;
        private readonly bool _isTcp;
        private readonly Action<TouchSocketConfig> _configureAction;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private IModbusMaster? _master;
        private ModbusTcpMaster? _tcpMaster;
        private ModbusRtuMaster? _rtuMaster;
        private bool _configured;
        private bool _disposed;

        /// <summary>
        /// 初始化智嵌 Modbus TCP 客户端适配器。
        /// </summary>
        /// <param name="host">设备 IP 地址。</param>
        /// <param name="port">设备端口（1~65535）。</param>
        /// <param name="deviceAddress">站号（1~247）。</param>
        /// <param name="commandTimeoutMs">单命令超时（毫秒）。</param>
        /// <param name="retryCount">重试次数（不含首次）。</param>
        /// <param name="retryDelayMs">重试间隔（毫秒）。</param>
        public ZhiQianModbusClientAdapter(
            string host,
            int port,
            byte deviceAddress,
            int commandTimeoutMs,
            int retryCount,
            int retryDelayMs)
            : this(
                true,
                deviceAddress,
                commandTimeoutMs,
                retryCount,
                retryDelayMs,
                config => config.SetRemoteIPHost(new IPHost($"{host}:{port}"))) {
        }

        /// <summary>
        /// 初始化智嵌 Modbus RTU 客户端适配器。
        /// </summary>
        /// <param name="portName">串口名称。</param>
        /// <param name="baudRate">波特率。</param>
        /// <param name="parity">校验位。</param>
        /// <param name="dataBits">数据位。</param>
        /// <param name="stopBits">停止位。</param>
        /// <param name="deviceAddress">站号（1~247）。</param>
        /// <param name="commandTimeoutMs">单命令超时（毫秒）。</param>
        /// <param name="retryCount">重试次数（不含首次）。</param>
        /// <param name="retryDelayMs">重试间隔（毫秒）。</param>
        public ZhiQianModbusClientAdapter(
            string portName,
            int baudRate,
            Parity parity,
            int dataBits,
            StopBits stopBits,
            byte deviceAddress,
            int commandTimeoutMs,
            int retryCount,
            int retryDelayMs)
            : this(
                false,
                deviceAddress,
                commandTimeoutMs,
                retryCount,
                retryDelayMs,
                config => config.SetSerialPortOption(opt => {
                    opt.PortName = portName;
                    opt.BaudRate = baudRate;
                    opt.Parity = parity;
                    opt.DataBits = dataBits;
                    opt.StopBits = stopBits;
                })) {
        }

        /// <summary>
        /// 初始化智嵌 Modbus 客户端适配器（测试注入）。
        /// </summary>
        /// <param name="master">预配置的 Modbus 主站实例。</param>
        /// <param name="deviceAddress">站号（1~247）。</param>
        /// <param name="commandTimeoutMs">单命令超时（毫秒）。</param>
        /// <param name="retryCount">重试次数（不含首次）。</param>
        /// <param name="retryDelayMs">重试间隔（毫秒）。</param>
        internal ZhiQianModbusClientAdapter(
            IModbusMaster master,
            byte deviceAddress,
            int commandTimeoutMs = 300,
            int retryCount = 2,
            int retryDelayMs = 50)
            : this(
                master is ModbusTcpMaster,
                deviceAddress,
                commandTimeoutMs,
                retryCount,
                retryDelayMs,
                _ => { }) {
            _master = master;
            if (master is ModbusTcpMaster tcp) {
                _tcpMaster = tcp;
            }
            else if (master is ModbusRtuMaster rtu) {
                _rtuMaster = rtu;
            }

            _configured = true;
        }

        private ZhiQianModbusClientAdapter(
            bool isTcp,
            byte deviceAddress,
            int commandTimeoutMs,
            int retryCount,
            int retryDelayMs,
            Action<TouchSocketConfig> configureAction) {
            // 步骤1：校验关键参数边界，避免延迟至首次操作才暴露非法配置。
            if (deviceAddress is 0 or > 247) {
                throw new ArgumentOutOfRangeException(nameof(deviceAddress), "站号必须在 1~247 范围。");
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

            // 步骤2：初始化基础字段与重试策略（写策略略多于读策略）。
            _isTcp = isTcp;
            _deviceAddress = deviceAddress;
            _commandTimeoutMs = commandTimeoutMs;
            _configureAction = configureAction;
            _writeRetryPolicy = Policy
                .Handle<Exception>(ex => ex is not OperationCanceledException)
                .WaitAndRetryAsync(
                    retryCount,
                    attempt => TimeSpan.FromMilliseconds(retryDelayMs * attempt),
                    (ex, _, attempt, _) => Log.Warn(ex, "ZhiQian写重试 slaveId={0} attempt={1} ex={2}", _deviceAddress, attempt, ex.Message));
            _readRetryPolicy = Policy
                .Handle<Exception>(ex => ex is not OperationCanceledException)
                .WaitAndRetryAsync(
                    Math.Max(0, retryCount - 1),
                    attempt => TimeSpan.FromMilliseconds(retryDelayMs * attempt),
                    (ex, _, attempt, _) => Log.Warn(ex, "ZhiQian读重试 slaveId={0} attempt={1} ex={2}", _deviceAddress, attempt, ex.Message));
        }

        /// <inheritdoc />
        public bool IsConnected {
            get {
                if (_tcpMaster is not null) return _tcpMaster.Online;
                if (_rtuMaster is not null) return _rtuMaster.Online;
                return false;
            }
        }

        /// <inheritdoc />
        public async ValueTask ConnectAsync(CancellationToken cancellationToken = default) {
            // 步骤1：等待门控，防止并发重入。
            // 步骤2：首次配置主站链路参数；已连接时直接返回。
            // 步骤3：执行连接，记录日志。
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                ThrowIfDisposed();
                if (IsConnected) {
                    return;
                }

                if (!_configured) {
                    var cfg = new TouchSocketConfig();
                    _configureAction(cfg);
                    if (_isTcp) {
                        var tcp = new ModbusTcpMaster();
                        await tcp.SetupAsync(cfg).ConfigureAwait(false);
                        _tcpMaster = tcp;
                        _master = tcp;
                    }
                    else {
                        var rtu = new ModbusRtuMaster();
                        await rtu.SetupAsync(cfg).ConfigureAwait(false);
                        _rtuMaster = rtu;
                        _master = rtu;
                    }

                    _configured = true;
                }

                var opId = OperationIdFactory.CreateShortOperationId();
                Log.Info("ZhiQian连接开始 opId={0} slaveId={1}", opId, _deviceAddress);
                if (_tcpMaster is not null) {
                    await _tcpMaster.ConnectAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (_rtuMaster is not null) {
                    await _rtuMaster.ConnectAsync(cancellationToken).ConfigureAwait(false);
                }

                Log.Info("ZhiQian连接完成 opId={0} slaveId={1}", opId, _deviceAddress);
            }
            finally {
                _gate.Release();
            }
        }

        /// <inheritdoc />
        public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default) {
            // 步骤1：等待门控，防止并发重入。
            // 步骤2：按传输模式调用 CloseAsync 断开链路。
            cancellationToken.ThrowIfCancellationRequested();
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                if (!IsConnected) {
                    return;
                }

                if (_tcpMaster is not null) {
                    await _tcpMaster.CloseAsync("主动断开", cancellationToken).ConfigureAwait(false);
                }
                else if (_rtuMaster is not null) {
                    await _rtuMaster.CloseAsync("主动断开", cancellationToken).ConfigureAwait(false);
                }

                Log.Info("ZhiQian已断开 slaveId={0}", _deviceAddress);
            }
            finally {
                _gate.Release();
            }
        }

        /// <inheritdoc />
        public async ValueTask<IReadOnlyList<bool>> ReadDoStatesAsync(CancellationToken cancellationToken = default) {
            // 步骤1：校验连接状态，避免无效读取。
            // 步骤2：使用 Polly 重试策略封装 FC01 批量读线圈（Y01~Y32，地址 0~31）。
            // 步骤3：将 ReadOnlyMemory<bool> 转换为 bool 数组并返回。
            cancellationToken.ThrowIfCancellationRequested();
            var master = GetConnectedMaster();
            bool[]? result = null;
            await _readRetryPolicy.ExecuteAsync(async ct => {
                var memory = await master.ReadCoilsAsync(
                    _deviceAddress,
                    ZhiQianAddressMap.ToCoilAddress(ZhiQianAddressMap.DoIndexMin),
                    (ushort)ZhiQianAddressMap.DoChannelCount,
                    _commandTimeoutMs,
                    ct).ConfigureAwait(false);
                if (memory.Length < ZhiQianAddressMap.DoChannelCount) {
                    throw new InvalidDataException($"读 DO 响应长度不足，期望 {ZhiQianAddressMap.DoChannelCount}，实际 {memory.Length}。");
                }

                result = memory.ToArray();
            }, cancellationToken).ConfigureAwait(false);
            return result ?? Array.Empty<bool>();
        }

        /// <inheritdoc />
        public async ValueTask WriteSingleDoAsync(int doIndex, bool isOn, CancellationToken cancellationToken = default) {
            // 步骤1：校验 Y 路范围。
            // 步骤2：换算线圈地址。
            // 步骤3：使用 Polly 重试策略执行 FC05 写单线圈。
            cancellationToken.ThrowIfCancellationRequested();
            var coilAddress = ZhiQianAddressMap.ToCoilAddress(doIndex);
            var master = GetConnectedMaster();
            await _writeRetryPolicy.ExecuteAsync(async ct => {
                var response = await master.WriteSingleCoilAsync(
                    _deviceAddress,
                    coilAddress,
                    isOn,
                    _commandTimeoutMs,
                    ct).ConfigureAwait(false);
                if (!response.IsSuccess) {
                    throw new InvalidOperationException($"写 DO Y{doIndex:D2} 失败，ErrorCode={response.ErrorCode}。");
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async ValueTask WriteBatchDoAsync(IReadOnlyDictionary<int, bool> doStates, CancellationToken cancellationToken = default) {
            // 步骤1：校验所有 Y 路范围，校验失败立即抛出，不进行部分写入。
            // 步骤2：先回读当前 32 路状态作为基准，合并目标变更。
            // 步骤3：使用 FC0F 一次写入全量 32 路，减少往返次数。
            cancellationToken.ThrowIfCancellationRequested();
            if (doStates.Count == 0) {
                return;
            }

            foreach (var (doIndex, _) in doStates) {
                ZhiQianAddressMap.ToCoilAddress(doIndex);
            }

            var master = GetConnectedMaster();
            await _writeRetryPolicy.ExecuteAsync(async ct => {
                var current = await ReadDoStatesAsync(ct).ConfigureAwait(false);
                var target = current.ToArray();
                foreach (var (doIndex, isOn) in doStates) {
                    target[doIndex - ZhiQianAddressMap.DoIndexMin] = isOn;
                }

                var response = await master.WriteMultipleCoilsAsync(
                    _deviceAddress,
                    ZhiQianAddressMap.ToCoilAddress(ZhiQianAddressMap.DoIndexMin),
                    target.AsMemory(),
                    _commandTimeoutMs,
                    ct).ConfigureAwait(false);
                if (!response.IsSuccess) {
                    throw new InvalidOperationException($"批量写 DO 失败，ErrorCode={response.ErrorCode}。");
                }
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
                Log.Error(ex, "ZhiQian释放时断开异常 slaveId={0}", _deviceAddress);
            }

            _gate.Dispose();
        }

        /// <summary>
        /// 获取已连接的 Modbus Master，未连接时抛出 InvalidOperationException。
        /// </summary>
        /// <returns>当前 Modbus Master 实例。</returns>
        private IModbusMaster GetConnectedMaster() {
            ThrowIfDisposed();
            if (_master is null || !IsConnected) {
                throw new InvalidOperationException("智嵌设备未连接，请先调用 ConnectAsync。");
            }

            return _master;
        }

        /// <summary>
        /// 检查实例是否已被释放，已释放时抛出 ObjectDisposedException。
        /// </summary>
        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(ZhiQianModbusClientAdapter));
            }
        }
    }
}
