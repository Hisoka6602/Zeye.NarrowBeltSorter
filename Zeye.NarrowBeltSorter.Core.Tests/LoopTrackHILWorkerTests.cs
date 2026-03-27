using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Utilities;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// LoopTrackHILWorker 上机联调流程测试。
    /// </summary>
    public sealed class LoopTrackHILWorkerTests {
        /// <summary>
        /// 测试循环超时时间（毫秒）：用于覆盖短周期异步循环并预留调度缓冲，降低偶发超时抖动。
        /// </summary>
        private const int DefaultTestTimeoutMs = 80;

        /// <summary>
        /// HIL 开关关闭时不应执行硬件流程。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenHilDisabled_ShouldNotRunHardwareFlow() {
            var options = CreateValidOptions();
            options.Hil.Enabled = false;
            options.Enabled = false;
            var manager = new FakeLoopTrackManager();
            var worker = CreateWorker(options, manager);

            await worker.RunForTestAsync(CancellationToken.None);

            Assert.Equal(0, worker.CreateManagerCallCount);
            Assert.Equal(0, manager.ConnectCallCount);
        }

        /// <summary>
        /// HIL 自动连接、设速与自动启动链路应按配置执行。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenAutoConnectSetTargetAndAutoStart_ShouldRunBootPipeline() {
            var options = CreateValidOptions();
            options.Hil.Enabled = true;
            options.Hil.AutoConnectOnStart = true;
            options.Hil.AutoSetInitialTargetAfterConnect = true;
            options.TargetSpeedMmps = 1200m;
            options.Hil.AutoStartAfterConnect = true;
            options.Hil.StatusLogIntervalMs = 10;
            var manager = new FakeLoopTrackManager();
            using var cts = new CancellationTokenSource(DefaultTestTimeoutMs);
            var worker = CreateWorker(options, manager);

            await worker.RunForTestAsync(cts.Token);

            Assert.Equal(1, manager.ConnectCallCount);
            Assert.Equal(1, manager.SetTargetSpeedCallCount);
            Assert.Equal(1, manager.StartCallCount);
            Assert.Equal(1200m, manager.TargetSpeedMmps);
        }

        /// <summary>
        /// AutoStart 失败时应触发 Stop/Disconnect/Dispose 补偿链路。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenAutoStartFailed_ShouldCompensateStopDisconnectDispose() {
            var options = CreateValidOptions();
            options.Hil.Enabled = true;
            options.Hil.AutoConnectOnStart = true;
            options.Hil.AutoStartAfterConnect = true;
            options.Hil.StatusLogIntervalMs = 10;
            var manager = new FakeLoopTrackManager {
                StartResult = false
            };
            var worker = CreateWorker(options, manager);

            await worker.RunForTestAsync(CancellationToken.None);

            Assert.Equal(1, manager.ConnectCallCount);
            Assert.Equal(1, manager.StopCallCount);
            Assert.Equal(1, manager.DisconnectCallCount);
            Assert.Equal(1, manager.DisposeCallCount);
        }

        /// <summary>
        /// 非交互环境启用键盘停轨时应自动降级且不崩溃。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenKeyboardStopEnabledInNonInteractiveEnvironment_ShouldDegradeSafely() {
            var options = CreateValidOptions();
            options.Hil.Enabled = true;
            options.Hil.EnableKeyboardStop = true;
            options.Hil.AutoConnectOnStart = true;
            options.Hil.StatusLogIntervalMs = 10;
            var manager = new FakeLoopTrackManager();
            using var cts = new CancellationTokenSource(DefaultTestTimeoutMs);
            var worker = CreateWorker(options, manager);

            var exception = await Record.ExceptionAsync(() => worker.RunForTestAsync(cts.Token));

            Assert.Null(exception);
            Assert.Equal(1, manager.ConnectCallCount);
        }

        /// <summary>
        /// 事件回调异常应被隔离，不应拖垮主循环。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenEventCallbackThrows_ShouldBeIsolated() {
            var options = CreateValidOptions();
            options.Hil.Enabled = true;
            options.Hil.AutoConnectOnStart = true;
            options.Hil.StatusLogIntervalMs = 10;
            var manager = new FakeLoopTrackManager {
                RaiseConnectionStatusChangedOnConnect = true
            };
            using var cts = new CancellationTokenSource(DefaultTestTimeoutMs);
            var worker = CreateWorker(options, manager);
            worker.ThrowOnConnectionStatusChanged = true;

            var exception = await Record.ExceptionAsync(() => worker.RunForTestAsync(cts.Token));

            Assert.Null(exception);
            Assert.Equal(1, manager.ConnectCallCount);
            Assert.Equal(0, manager.StopCallCount);
        }

        /// <summary>
        /// 非法配置应安全退出。
        /// </summary>
        /// <param name="mutate">配置变更动作。</param>
        [Theory]
        [MemberData(nameof(GetInvalidConfigurations))]
        public async Task ExecuteAsync_WhenConfigurationInvalid_ShouldExitSafely(Action<LoopTrackServiceOptions> mutate) {
            var options = CreateValidOptions();
            mutate(options);
            var manager = new FakeLoopTrackManager();
            var worker = CreateWorker(options, manager);

            var exception = await Record.ExceptionAsync(() => worker.RunForTestAsync(CancellationToken.None));

            Assert.Null(exception);
            Assert.Equal(0, manager.ConnectCallCount);
        }

        /// <summary>
        /// 返回非法配置集合。
        /// </summary>
        /// <returns>测试数据。</returns>
        public static IEnumerable<object[]> GetInvalidConfigurations() {
            yield return [new Action<LoopTrackServiceOptions>(o => o.Hil.StatusLogIntervalMs = 0)];
            yield return [new Action<LoopTrackServiceOptions>(o => o.TargetSpeedMmps = -1m)];
            yield return [new Action<LoopTrackServiceOptions>(o => o.TargetSpeedMmps = 999999m)];
            yield return [new Action<LoopTrackServiceOptions>(o => o.Hil.ConnectRetryDelayMs = 0)];
            yield return [new Action<LoopTrackServiceOptions>(o => o.Hil.ConnectMaxAttempts = -1)];
            yield return [new Action<LoopTrackServiceOptions>(o => o.Hil.ConnectMaxAttempts = 21)];
            yield return [new Action<LoopTrackServiceOptions>(o => o.Hil.StopKey = "InvalidKey")];
        }

        /// <summary>
        /// 创建测试 Worker。
        /// </summary>
        /// <param name="options">配置。</param>
        /// <param name="manager">管理器。</param>
        /// <returns>Worker 实例。</returns>
        private static TestableLoopTrackHILWorker CreateWorker(
            LoopTrackServiceOptions options,
            ILoopTrackManager manager) {
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            return new TestableLoopTrackHILWorker(
                NullLogger<Zeye.NarrowBeltSorter.Host.Services.LoopTrackManagerService>.Instance,
                safeExecutor,
                Microsoft.Extensions.Options.Options.Create(options),
                manager);
        }

        /// <summary>
        /// 创建有效配置。
        /// </summary>
        /// <returns>默认有效配置。</returns>
        private static LoopTrackServiceOptions CreateValidOptions() {
            // 步骤1：构造环轨基础配置，保证单元测试拥有可执行的最小主服务参数。
            return new LoopTrackServiceOptions {
                Enabled = false,
                TrackName = "HIL-Test-Track",
                AutoStart = false,
                TargetSpeedMmps = 0m,
                PollingIntervalMs = 100,
                LeiMaConnection = new LoopTrackLeiMaConnectionOptions {
                    Transport = "TcpGateway",
                    RemoteHost = "127.0.0.1:502",
                    SlaveAddresses = [1],
                    TimeoutMs = 1000,
                    RetryCount = 1,
                    MaxOutputHz = 25m,
                    MaxTorqueRawUnit = 1000,
                    TorqueSetpointWriteIntervalMs = 200
                },
                Pid = new LoopTrackPidOptions {
                    Enabled = true,
                    Kp = 0.28m,
                    Ki = 0.028m,
                    Kd = 0.005m,
                    OutputMinRaw = 0m,
                    OutputMaxRaw = 25m,
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
                    EnableVerboseStatus = false,
                    EnableRealtimeSpeedLog = false,
                    EnablePidTuningLog = false,
                    InfoStatusIntervalMs = 1000,
                    DebugStatusIntervalMs = 1000,
                    RealtimeSpeedLogIntervalMs = 1000,
                    PidTuningLogIntervalMs = 1000,
                    UnstableDeviationThresholdMmps = 50m,
                    UnstableDurationMs = 1000
                },
                Hil = new LoopTrackHilOptions {
                    Enabled = true,
                    StatusLogIntervalMs = 20,
                    EnableKeyboardStop = false,
                    KeyboardStopPollingIntervalMs = 100,
                    StopKey = "S",
                    AutoConnectOnStart = true,
                    AutoClearAlarmAfterConnect = false,
                    AutoSetInitialTargetAfterConnect = false,
                    AutoStartAfterConnect = false,
                    ConnectMaxAttempts = 0,
                    ConnectRetryDelayMs = 20,
                    EnableVerboseEventLog = true
                }
            };
        }
    }
}
