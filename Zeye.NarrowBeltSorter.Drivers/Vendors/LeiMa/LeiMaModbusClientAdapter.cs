using System.IO.Ports;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa {
    /// <summary>
    /// 雷玛 Modbus RTU 客户端适配器实现。
    /// </summary>
    public sealed class LeiMaModbusClientAdapter : ILeiMaModbusClientAdapter {
        private const byte ReadHoldingRegistersFunctionCode = 0x03;
        private const byte WriteSingleRegisterFunctionCode = 0x06;

        private readonly Func<SerialPort> _serialPortFactory;
        private readonly byte _slaveAddress;
        private readonly object _ioLock = new();

        private SerialPort? _serialPort;
        private bool _disposed;

        /// <summary>
        /// 初始化雷玛 Modbus RTU 客户端适配器。
        /// </summary>
        /// <param name="portName">串口名称。</param>
        /// <param name="slaveAddress">从站地址（1~247）。</param>
        /// <param name="baudRate">波特率。</param>
        /// <param name="parity">校验位。</param>
        /// <param name="dataBits">数据位。</param>
        /// <param name="stopBits">停止位。</param>
        /// <param name="readTimeoutMilliseconds">读超时（毫秒）。</param>
        /// <param name="writeTimeoutMilliseconds">写超时（毫秒）。</param>
        public LeiMaModbusClientAdapter(
            string portName,
            byte slaveAddress,
            int baudRate = 9600,
            Parity parity = Parity.None,
            int dataBits = 8,
            StopBits stopBits = StopBits.One,
            int readTimeoutMilliseconds = 1000,
            int writeTimeoutMilliseconds = 1000)
            : this(() => CreateSerialPort(
                portName,
                baudRate,
                parity,
                dataBits,
                stopBits,
                readTimeoutMilliseconds,
                writeTimeoutMilliseconds), slaveAddress) {
        }

        /// <summary>
        /// 初始化雷玛 Modbus RTU 客户端适配器（用于测试注入）。
        /// </summary>
        /// <param name="serialPortFactory">串口工厂。</param>
        /// <param name="slaveAddress">从站地址（1~247）。</param>
        internal LeiMaModbusClientAdapter(Func<SerialPort> serialPortFactory, byte slaveAddress) {
            _serialPortFactory = serialPortFactory ?? throw new ArgumentNullException(nameof(serialPortFactory));
            if (slaveAddress is 0 or > 247) {
                throw new ArgumentOutOfRangeException(nameof(slaveAddress), "从站地址必须在 1~247 范围。");
            }

            _slaveAddress = slaveAddress;
        }

        /// <inheritdoc />
        public bool IsConnected {
            get {
                lock (_ioLock) {
                    return _serialPort?.IsOpen == true;
                }
            }
        }

        /// <inheritdoc />
        public ValueTask ConnectAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            lock (_ioLock) {
                if (_serialPort?.IsOpen == true) {
                    return ValueTask.CompletedTask;
                }

                _serialPort?.Dispose();
                var serialPort = _serialPortFactory();
                serialPort.Open();
                serialPort.DiscardInBuffer();
                serialPort.DiscardOutBuffer();
                _serialPort = serialPort;
            }

            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_ioLock) {
                if (_serialPort is null) {
                    return ValueTask.CompletedTask;
                }

                if (_serialPort.IsOpen) {
                    _serialPort.Close();
                }
            }

            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask<ushort> ReadHoldingRegisterAsync(ushort address, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            var request = BuildReadHoldingRegisterFrame(_slaveAddress, address);
            var responseLength = 7;

            // 步骤1：发送 03 功能码请求帧。
            // 步骤2：接收固定长度响应并校验 CRC 与功能码。
            // 步骤3：解析寄存器值并返回。
            var response = Exchange(request, responseLength);
            ValidateReadResponse(_slaveAddress, response);
            var value = (ushort)((response[3] << 8) | response[4]);

            return ValueTask.FromResult(value);
        }

        /// <inheritdoc />
        public ValueTask WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            var request = BuildWriteSingleRegisterFrame(_slaveAddress, address, value);
            var responseLength = 8;

            // 步骤1：发送 06 功能码请求帧。
            // 步骤2：接收回显响应并校验 CRC 与地址/值一致性。
            var response = Exchange(request, responseLength);
            ValidateWriteResponse(_slaveAddress, request, response);

            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() {
            if (_disposed) {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            lock (_ioLock) {
                if (_serialPort is not null) {
                    if (_serialPort.IsOpen) {
                        _serialPort.Close();
                    }

                    _serialPort.Dispose();
                    _serialPort = null;
                }
            }

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// 执行串口请求响应交换。
        /// </summary>
        /// <param name="request">请求帧。</param>
        /// <param name="responseLength">预期响应长度。</param>
        /// <returns>响应帧。</returns>
        private byte[] Exchange(byte[] request, int responseLength) {
            lock (_ioLock) {
                ThrowIfDisposed();
                if (_serialPort is null || !_serialPort.IsOpen) {
                    throw new InvalidOperationException("串口未连接。");
                }

                _serialPort.DiscardInBuffer();
                _serialPort.Write(request, 0, request.Length);
                return ReadExactly(_serialPort, responseLength);
            }
        }

        /// <summary>
        /// 校验读保持寄存器响应。
        /// </summary>
        /// <param name="slaveAddress">期望从站地址。</param>
        /// <param name="response">响应帧。</param>
        private static void ValidateReadResponse(byte slaveAddress, byte[] response) {
            if (response.Length != 7) {
                throw new InvalidDataException("读取响应长度错误。");
            }

            ValidateResponseHeader(slaveAddress, ReadHoldingRegistersFunctionCode, response);

            if (response[2] != 0x02) {
                throw new InvalidDataException("读取响应字节数错误。");
            }
        }

        /// <summary>
        /// 校验写单寄存器响应。
        /// </summary>
        /// <param name="slaveAddress">期望从站地址。</param>
        /// <param name="request">请求帧。</param>
        /// <param name="response">响应帧。</param>
        private static void ValidateWriteResponse(byte slaveAddress, byte[] request, byte[] response) {
            if (response.Length != 8) {
                throw new InvalidDataException("写入响应长度错误。");
            }

            ValidateResponseHeader(slaveAddress, WriteSingleRegisterFunctionCode, response);

            for (var i = 0; i < 6; i++) {
                if (request[i] != response[i]) {
                    throw new InvalidDataException("写入响应与请求回显不一致。");
                }
            }
        }

        /// <summary>
        /// 校验响应头与 CRC。
        /// </summary>
        /// <param name="slaveAddress">期望从站地址。</param>
        /// <param name="functionCode">期望功能码。</param>
        /// <param name="response">响应帧。</param>
        private static void ValidateResponseHeader(byte slaveAddress, byte functionCode, byte[] response) {
            if (response[0] != slaveAddress) {
                throw new InvalidDataException("响应从站地址不匹配。");
            }

            if (response[1] == (functionCode | 0x80)) {
                throw new InvalidDataException($"设备返回异常码：0x{response[2]:X2}。");
            }

            if (response[1] != functionCode) {
                throw new InvalidDataException("响应功能码不匹配。");
            }

            var crc = ComputeCrc(response.AsSpan(0, response.Length - 2));
            if (response[^2] != (byte)(crc & 0xFF) || response[^1] != (byte)(crc >> 8)) {
                throw new InvalidDataException("响应 CRC 校验失败。");
            }
        }

        /// <summary>
        /// 构造 03 功能码读保持寄存器帧。
        /// </summary>
        /// <param name="slaveAddress">从站地址。</param>
        /// <param name="address">寄存器地址。</param>
        /// <returns>请求帧。</returns>
        private static byte[] BuildReadHoldingRegisterFrame(byte slaveAddress, ushort address) {
            var frame = new byte[8];
            frame[0] = slaveAddress;
            frame[1] = ReadHoldingRegistersFunctionCode;
            frame[2] = (byte)(address >> 8);
            frame[3] = (byte)(address & 0xFF);
            frame[4] = 0x00;
            frame[5] = 0x01;
            AppendCrc(frame);
            return frame;
        }

        /// <summary>
        /// 构造 06 功能码写单寄存器帧。
        /// </summary>
        /// <param name="slaveAddress">从站地址。</param>
        /// <param name="address">寄存器地址。</param>
        /// <param name="value">写入值。</param>
        /// <returns>请求帧。</returns>
        private static byte[] BuildWriteSingleRegisterFrame(byte slaveAddress, ushort address, ushort value) {
            var frame = new byte[8];
            frame[0] = slaveAddress;
            frame[1] = WriteSingleRegisterFunctionCode;
            frame[2] = (byte)(address >> 8);
            frame[3] = (byte)(address & 0xFF);
            frame[4] = (byte)(value >> 8);
            frame[5] = (byte)(value & 0xFF);
            AppendCrc(frame);
            return frame;
        }

        /// <summary>
        /// 向帧尾追加 CRC（低字节在前）。
        /// </summary>
        /// <param name="frame">帧数据。</param>
        private static void AppendCrc(byte[] frame) {
            var crc = ComputeCrc(frame.AsSpan(0, frame.Length - 2));
            frame[^2] = (byte)(crc & 0xFF);
            frame[^1] = (byte)(crc >> 8);
        }

        /// <summary>
        /// 计算 Modbus RTU CRC16。
        /// </summary>
        /// <param name="data">待校验数据。</param>
        /// <returns>CRC16。</returns>
        private static ushort ComputeCrc(ReadOnlySpan<byte> data) {
            ushort crc = 0xFFFF;
            foreach (var value in data) {
                crc ^= value;
                for (var i = 0; i < 8; i++) {
                    if ((crc & 0x0001) != 0) {
                        crc >>= 1;
                        crc ^= 0xA001;
                    }
                    else {
                        crc >>= 1;
                    }
                }
            }

            return crc;
        }

        /// <summary>
        /// 从串口读取指定长度字节。
        /// </summary>
        /// <param name="serialPort">串口对象。</param>
        /// <param name="length">目标长度。</param>
        /// <returns>读取到的字节数组。</returns>
        private static byte[] ReadExactly(SerialPort serialPort, int length) {
            var buffer = new byte[length];
            var offset = 0;

            while (offset < length) {
                var read = serialPort.Read(buffer, offset, length - offset);
                if (read <= 0) {
                    throw new TimeoutException("串口读取超时。");
                }

                offset += read;
            }

            return buffer;
        }

        /// <summary>
        /// 创建串口对象。
        /// </summary>
        /// <param name="portName">串口名称。</param>
        /// <param name="baudRate">波特率。</param>
        /// <param name="parity">校验位。</param>
        /// <param name="dataBits">数据位。</param>
        /// <param name="stopBits">停止位。</param>
        /// <param name="readTimeoutMilliseconds">读超时。</param>
        /// <param name="writeTimeoutMilliseconds">写超时。</param>
        /// <returns>串口对象。</returns>
        private static SerialPort CreateSerialPort(
            string portName,
            int baudRate,
            Parity parity,
            int dataBits,
            StopBits stopBits,
            int readTimeoutMilliseconds,
            int writeTimeoutMilliseconds) {
            if (string.IsNullOrWhiteSpace(portName)) {
                throw new ArgumentException("串口名称不能为空。", nameof(portName));
            }

            if (baudRate <= 0) {
                throw new ArgumentOutOfRangeException(nameof(baudRate), "波特率必须大于 0。");
            }

            if (dataBits <= 0) {
                throw new ArgumentOutOfRangeException(nameof(dataBits), "数据位必须大于 0。");
            }

            if (readTimeoutMilliseconds <= 0 || writeTimeoutMilliseconds <= 0) {
                throw new ArgumentOutOfRangeException(nameof(readTimeoutMilliseconds), "读写超时必须大于 0。");
            }

            return new SerialPort(portName, baudRate, parity, dataBits, stopBits) {
                ReadTimeout = readTimeoutMilliseconds,
                WriteTimeout = writeTimeoutMilliseconds
            };
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
