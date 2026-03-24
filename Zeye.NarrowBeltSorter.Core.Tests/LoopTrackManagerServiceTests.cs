using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa;
using Zeye.NarrowBeltSorter.Host.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Host.Servers;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// LoopTrackManagerService 连接模式与补偿链路测试。
    /// </summary>
    public sealed class LoopTrackManagerServiceTests {
        /// <summary>
        /// Transport=TcpGateway 时应创建并走 RemoteHost 路径。
        /// </summary>
        [Fact]
        public async Task CreateAdapter_WhenTransportIsTcpGateway_ShouldUseRemoteHostPath() {
            var service = CreateService();
            var connection = new LoopTrackLeiMaConnectionOptions {
                Transport = LoopTrackLeiMaTransportModes.TcpGateway,
                RemoteHost = "127.0.0.1:502",
                SerialRtu = new LoopTrackLeiMaSerialRtuOptions {
                    PortName = string.Empty,
                    BaudRate = 0,
                    DataBits = 3,
                    StopBits = System.IO.Ports.StopBits.None
                }
            };

            var adapter = service.ExposeCreateAdapter(connection);

            await adapter.DisposeAsync();
            Assert.IsType<LeiMaModbusClientAdapter>(adapter);
        }

        /// <summary>
        /// Transport=SerialRtu 且参数合法时应可创建客户端。
        /// </summary>
        [Fact]
        public async Task CreateAdapter_WhenTransportIsSerialRtuAndValid_ShouldCreateClient() {
            var service = CreateService();
            var connection = new LoopTrackLeiMaConnectionOptions {
                Transport = LoopTrackLeiMaTransportModes.SerialRtu,
                RemoteHost = string.Empty,
                SerialRtu = new LoopTrackLeiMaSerialRtuOptions {
                    PortName = "COM3",
                    BaudRate = 19200,
                    Parity = System.IO.Ports.Parity.None,
                    DataBits = 8,
                    StopBits = System.IO.Ports.StopBits.One
                }
            };

            var adapter = service.ExposeCreateAdapter(connection);

            await adapter.DisposeAsync();
            Assert.IsType<LeiMaModbusClientAdapter>(adapter);
        }

        /// <summary>
        /// Transport=SerialRtu 且参数非法时服务应安全退出且不崩溃。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenSerialRtuConfigInvalid_ShouldExitSafely() {
            var options = CreateValidOptions();
            options.Enabled = true;
            options.LeiMaConnection.Transport = LoopTrackLeiMaTransportModes.SerialRtu;
            options.LeiMaConnection.RemoteHost = string.Empty;
            options.LeiMaConnection.SerialRtu.PortName = string.Empty;

            var service = CreateService(options);

            var exception = await Record.ExceptionAsync(() => service.RunForTestAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.Equal(0, service.CreateManagerCallCount);
        }

        /// <summary>
        /// Transport 为空时应在配置校验阶段安全退出。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenTransportIsEmpty_ShouldExitSafely() {
            var options = CreateValidOptions();
            options.Enabled = true;
            options.LeiMaConnection.Transport = string.Empty;

            var service = CreateService(options);

            var exception = await Record.ExceptionAsync(() => service.RunForTestAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.Equal(0, service.CreateManagerCallCount);
        }

        /// <summary>
        /// Transport 非法值时应在配置校验阶段安全退出。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenTransportIsInvalid_ShouldExitSafely() {
            var options = CreateValidOptions();
            options.Enabled = true;
            options.LeiMaConnection.Transport = "Unknown";

            var service = CreateService(options);

            var exception = await Record.ExceptionAsync(() => service.RunForTestAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.Equal(0, service.CreateManagerCallCount);
        }

        /// <summary>
        /// SerialRtu.BaudRate 非法时应在配置校验阶段安全退出。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenSerialBaudRateInvalid_ShouldExitSafely() {
            var options = CreateValidOptions();
            options.Enabled = true;
            options.LeiMaConnection.Transport = LoopTrackLeiMaTransportModes.SerialRtu;
            options.LeiMaConnection.SerialRtu.BaudRate = 0;

            var service = CreateService(options);

            var exception = await Record.ExceptionAsync(() => service.RunForTestAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.Equal(0, service.CreateManagerCallCount);
        }

        /// <summary>
        /// SerialRtu.DataBits 非法时应在配置校验阶段安全退出。
        /// </summary>
        [Theory]
        [InlineData(4)]
        [InlineData(9)]
        public async Task ExecuteAsync_WhenSerialDataBitsInvalid_ShouldExitSafely(int dataBits) {
            var options = CreateValidOptions();
            options.Enabled = true;
            options.LeiMaConnection.Transport = LoopTrackLeiMaTransportModes.SerialRtu;
            options.LeiMaConnection.SerialRtu.DataBits = dataBits;

            var service = CreateService(options);

            var exception = await Record.ExceptionAsync(() => service.RunForTestAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.Equal(0, service.CreateManagerCallCount);
        }

        /// <summary>
        /// SerialRtu.StopBits.None 非法时应在配置校验阶段安全退出。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenSerialStopBitsNone_ShouldExitSafely() {
            var options = CreateValidOptions();
            options.Enabled = true;
            options.LeiMaConnection.Transport = LoopTrackLeiMaTransportModes.SerialRtu;
            options.LeiMaConnection.SerialRtu.StopBits = System.IO.Ports.StopBits.None;

            var service = CreateService(options);

            var exception = await Record.ExceptionAsync(() => service.RunForTestAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.Equal(0, service.CreateManagerCallCount);
        }

        /// <summary>
        /// AutoStart 失败时应执行 Stop+Disconnect+Dispose 补偿链路。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenAutoStartFailed_ShouldCompensateStopDisconnectDispose() {
            var options = CreateValidOptions();
            options.Enabled = true;
            options.AutoStart = true;
            options.TargetSpeedMmps = 1200m;
            var manager = new FakeLoopTrackManager {
                StartResult = false
            };
            var service = CreateService(options, manager);

            await service.RunForTestAsync(CancellationToken.None);

            Assert.Equal(1, manager.ConnectCallCount);
            Assert.Equal(1, manager.StopCallCount);
            Assert.Equal(1, manager.DisconnectCallCount);
            Assert.Equal(1, manager.DisposeCallCount);
        }

        /// <summary>
        /// 创建测试服务实例。
        /// </summary>
        /// <param name="options">服务配置。</param>
        /// <param name="manager">可选管理器测试桩。</param>
        /// <returns>测试服务。</returns>
        private static TestableLoopTrackManagerService CreateService(
            LoopTrackServiceOptions? options = null,
            ILoopTrackManager? manager = null) {
            var safeExecutor = (SafeExecutor)Activator.CreateInstance(
                typeof(SafeExecutor),
                NullLogger<SafeExecutor>.Instance)!;
            return new TestableLoopTrackManagerService(
                NullLogger<LoopTrackManagerService>.Instance,
                safeExecutor,
                Microsoft.Extensions.Options.Options.Create(options ?? CreateValidOptions()),
                manager);
        }

        /// <summary>
        /// 创建默认有效配置。
        /// </summary>
        /// <returns>配置实例。</returns>
        private static LoopTrackServiceOptions CreateValidOptions() {
            // 步骤1：构建基础服务配置，确保测试拥有稳定默认值。
            // 步骤2：构建 LeiMa TCP/SerialRtu 默认连接参数，便于按用例覆写。
            // 步骤3：补齐重试与日志选项，避免与生产逻辑出现配置偏差。
            return new LoopTrackServiceOptions {
                Enabled = false,
                TrackName = "Test-Track",
                AutoStart = false,
                TargetSpeedMmps = 0m,
                PollingIntervalMs = 200,
                LeiMaConnection = new LoopTrackLeiMaConnectionOptions {
                    Transport = LoopTrackLeiMaTransportModes.TcpGateway,
                    RemoteHost = "127.0.0.1:502",
                    SlaveAddress = 1,
                    TimeoutMs = 1000,
                    RetryCount = 1,
                    MaxOutputHz = 25m,
                    MaxTorqueRawUnit = 1000,
                    SerialRtu = new LoopTrackLeiMaSerialRtuOptions {
                        PortName = "COM1",
                        BaudRate = 19200,
                        Parity = System.IO.Ports.Parity.None,
                        DataBits = 8,
                        StopBits = System.IO.Ports.StopBits.One
                    }
                },
                ConnectRetry = new LoopTrackConnectRetryOptions {
                    MaxAttempts = 0,
                    DelayMs = 50,
                    MaxDelayMs = 100
                },
                Logging = new LoopTrackLoggingOptions {
                    EnableVerboseStatus = false,
                    InfoStatusIntervalMs = 1000,
                    DebugStatusIntervalMs = 1000,
                    UnstableDeviationThresholdMmps = 50m,
                    UnstableDurationMs = 1000
                }
            };
        }
    }
}
