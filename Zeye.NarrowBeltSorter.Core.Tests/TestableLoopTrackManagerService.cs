using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Utilities.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Host.Services;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// 可测试化的 LoopTrackManagerService。
    /// </summary>
    internal sealed class TestableLoopTrackManagerService : LoopTrackManagerService {
        private readonly ILoopTrackManager? _testManager;

        /// <summary>
        /// 初始化可测试服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="safeExecutor">安全执行器。</param>
        /// <param name="options">配置。</param>
        /// <param name="manager">管理器测试桩。</param>
        public TestableLoopTrackManagerService(
            ILogger<LoopTrackManagerService> logger,
            SafeExecutor safeExecutor,
            IOptions<LoopTrackServiceOptions> options,
            ILoopTrackManager? manager = null)
            : base(logger, safeExecutor, options) {
            _testManager = manager;
        }

        /// <summary>
        /// 管理器创建次数。
        /// </summary>
        public int CreateManagerCallCount { get; private set; }

        /// <summary>
        /// 执行一次后台逻辑。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        public Task RunForTestAsync(CancellationToken stoppingToken) {
            return ExecuteAsync(stoppingToken);
        }

        /// <summary>
        /// 暴露适配器创建入口。
        /// </summary>
        /// <param name="connection">连接配置。</param>
        /// <returns>适配器实例。</returns>
        public ILeiMaModbusClientAdapter ExposeCreateAdapter(LoopTrackLeiMaConnectionOptions connection) {
            return CreateAdapter(connection);
        }

        /// <summary>
        /// 暴露配置校验入口。
        /// </summary>
        /// <param name="options">服务配置。</param>
        /// <param name="validationMessage">校验消息。</param>
        /// <returns>校验是否通过。</returns>
        public bool ExposeTryValidateOptions(LoopTrackServiceOptions options, out string validationMessage) {
            return TryValidateOptions(options, out validationMessage);
        }

        /// <inheritdoc />
        protected override ILoopTrackManager CreateManager(TimeSpan pollingInterval) {
            CreateManagerCallCount++;
            return _testManager ?? base.CreateManager(pollingInterval);
        }
    }
}
