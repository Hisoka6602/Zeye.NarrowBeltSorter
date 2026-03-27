using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Zeye.NarrowBeltSorter.Host.Services;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Utilities.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;

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
                SlaveAddresses = [1],
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
                SlaveAddresses = [1],
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
        /// 多从站单元素数组配置应通过校验。
        /// </summary>
        [Fact]
        public void ValidateOptions_WhenSlaveAddressesHasSingleItem_ShouldPass() {
            var service = CreateService();
            var options = CreateValidOptions();
            options.LeiMaConnection.SlaveAddresses = [1];

            var result = service.ExposeTryValidateOptions(options, out var validationMessage);

            Assert.True(result);
            Assert.Equal(string.Empty, validationMessage);
        }

        /// <summary>
        /// 从站地址为空数组时应校验失败。
        /// </summary>
        [Fact]
        public void ValidateOptions_WhenSlaveAddressesIsEmpty_ShouldFail() {
            var service = CreateService();
            var options = CreateValidOptions();
            options.LeiMaConnection.SlaveAddresses = [];

            var result = service.ExposeTryValidateOptions(options, out var validationMessage);

            Assert.False(result);
            Assert.Contains("SlaveAddresses", validationMessage);
        }

        /// <summary>
        /// 多从站单元素配置应可从配置源正确解析。
        /// </summary>
        [Fact]
        public void ConfigurationBinding_WhenSlaveAddressesHasSingleItem_ShouldParseCorrectly() {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> {
                    ["LoopTrack:LeiMaConnection:Transport"] = "TcpGateway",
                    ["LoopTrack:LeiMaConnection:RemoteHost"] = "127.0.0.1:502",
                    ["LoopTrack:LeiMaConnection:SlaveAddresses:0"] = "1"
                })
                .Build();
            var options = new LoopTrackServiceOptions();
            options.LeiMaConnection.SlaveAddresses = [];
            configuration.GetSection("LoopTrack").Bind(options);

            Assert.Single(options.LeiMaConnection.SlaveAddresses);
            Assert.Equal((byte)1, options.LeiMaConnection.SlaveAddresses[0]);
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
        /// AutoStart 成功时应先设速再启动，避免先启动读取历史频率导致瞬时超速。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenAutoStartSucceeded_ShouldSetSpeedBeforeStart() {
            var options = CreateValidOptions();
            options.Enabled = true;
            options.AutoStart = true;
            options.TargetSpeedMmps = 1000m;
            var manager = new FakeLoopTrackManager {
                StartResult = true,
                SetTargetSpeedResult = true
            };
            var service = CreateService(options, manager);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await service.RunForTestAsync(cts.Token);

            Assert.True(manager.CallSequence.Count >= 2);
            Assert.Equal(nameof(FakeLoopTrackManager.StartAsync), manager.CallSequence[0]);
            Assert.Equal(nameof(FakeLoopTrackManager.SetTargetSpeedAsync), manager.CallSequence[1]);
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
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
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
                    SlaveAddresses = [1],
                    TimeoutMs = 1000,
                    RetryCount = 1,
                    MaxOutputHz = 25m,
                    MaxTorqueRawUnit = 1000,
                    TorqueSetpointWriteIntervalMs = 200,
                    SerialRtu = new LoopTrackLeiMaSerialRtuOptions {
                        PortName = "COM1",
                        BaudRate = 19200,
                        Parity = System.IO.Ports.Parity.None,
                        DataBits = 8,
                        StopBits = System.IO.Ports.StopBits.One
                    }
                },
                Pid = new LoopTrackPidOptions {
                    Enabled = true,
                    Kp = 0.28m,
                    Ki = 0.028m,
                    Kd = 0.005m,
                    OutputMinHz = 0m,
                    OutputMaxHz = 25m,
                    IntegralMin = -10m,
                    IntegralMax = 10m,
                    DerivativeFilterAlpha = 0.2m,
                    FreezeIntegralWhenNotRunning = true
                },
                ConnectRetry = new LoopTrackConnectRetryOptions {
                    MaxAttempts = 0,
                    DelayMs = 50,
                    MaxDelayMs = 100
                },
                Logging = new LoopTrackLoggingOptions {
                    ConsoleMinLevel = "Warning",
                    EnableVerboseStatus = false,
                    EnableRealtimeSpeedLog = true,
                    EnablePidTuningLog = true,
                    InfoStatusIntervalMs = 1000,
                    DebugStatusIntervalMs = 1000,
                    RealtimeSpeedLogIntervalMs = 1000,
                    PidTuningLogIntervalMs = 1000,
                    UnstableDeviationThresholdMmps = 50m,
                    UnstableDurationMs = 1000
                }
            };
        }
    }
}
