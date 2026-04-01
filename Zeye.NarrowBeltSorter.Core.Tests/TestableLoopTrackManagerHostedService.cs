using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Utilities.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Execution.Services;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// 可测试化的 LoopTrackManagerHostedService。
    /// </summary>
    internal sealed class TestableLoopTrackManagerHostedService : LoopTrackManagerHostedService {
        private readonly ILoopTrackManager? _testManager;

        /// <summary>
        /// 初始化可测试服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="safeExecutor">安全执行器。</param>
        /// <param name="options">配置。</param>
        /// <param name="manager">管理器测试桩。</param>
        public TestableLoopTrackManagerHostedService(
            ILogger<Zeye.NarrowBeltSorter.Execution.Services.LoopTrackManagerHostedService> logger,
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

        /// <summary>
        /// 暴露连接重试执行入口。
        /// </summary>
        /// <param name="stage">阶段标识。</param>
        /// <param name="connectAction">连接动作。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>连接是否成功。</returns>
        public Task<bool> ExposeExecuteConnectWithRetryPolicyAsync(
            string stage,
            Func<CancellationToken, Task<(bool Success, bool Result)>> connectAction,
            CancellationToken cancellationToken = default) {
            return ExecuteConnectWithRetryPolicyAsync(
                totalAttempts: 2,
                initialDelayMs: 10,
                maxDelayMs: 20,
                useExponentialBackoff: false,
                logSubject: "LoopTrackTest",
                stage: stage,
                transport: "SerialRtu",
                connectAction: connectAction,
                stoppingToken: cancellationToken);
        }

        /// <inheritdoc />
        protected override ILoopTrackManager CreateManager(TimeSpan pollingInterval) {
            CreateManagerCallCount++;
            return _testManager ?? base.CreateManager(pollingInterval);
        }
    }
}
