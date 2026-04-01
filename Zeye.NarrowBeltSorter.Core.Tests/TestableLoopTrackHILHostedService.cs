using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Events.Track;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Execution.Services;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// 可测试化的 LoopTrackHILHostedService。
    /// </summary>
    internal sealed class TestableLoopTrackHILHostedService : LoopTrackHILHostedService {
        private readonly ILoopTrackManager? _testManager;

        /// <summary>
        /// 初始化可测试 HIL 服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="safeExecutor">安全执行器。</param>
        /// <param name="options">配置。</param>
        /// <param name="manager">管理器测试桩。</param>
        public TestableLoopTrackHILHostedService(
            ILogger<Zeye.NarrowBeltSorter.Execution.Services.LoopTrackHILHostedService> logger,
            SafeExecutor safeExecutor,
            IOptions<LoopTrackServiceOptions> options,
            ISystemStateManager systemStateManager,
            ILoopTrackManager? manager = null)
            : base(logger, safeExecutor, OptionsMonitorTestHelper.Create(options.Value), systemStateManager) {
            _testManager = manager;
        }

        /// <summary>
        /// 管理器创建次数。
        /// </summary>
        public int CreateManagerCallCount { get; private set; }

        /// <summary>
        /// 是否在连接状态事件回调中模拟抛出异常。
        /// </summary>
        public bool ThrowOnConnectionStatusChanged { get; set; }

        /// <summary>
        /// 运行一次 HIL 服务逻辑。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        public Task RunForTestAsync(CancellationToken stoppingToken) {
            return ExecuteAsync(stoppingToken);
        }

        /// <inheritdoc />
        protected override ILoopTrackManager CreateManager(TimeSpan pollingInterval) {
            CreateManagerCallCount++;
            return _testManager ?? base.CreateManager(pollingInterval);
        }

        /// <inheritdoc />
        protected override void OnConnectionStatusChanged(LoopTrackConnectionStatusChangedEventArgs args) {
            if (ThrowOnConnectionStatusChanged) {
                throw new InvalidOperationException("Simulated callback error");
            }

            base.OnConnectionStatusChanged(args);
        }
    }
}
