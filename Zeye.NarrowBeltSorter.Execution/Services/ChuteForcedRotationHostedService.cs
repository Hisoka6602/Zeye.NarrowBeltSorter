using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Enums.Device;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;


namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 格口强排轮转后台服务。
    /// 依赖 <see cref="ChuteForcedRotationOptions"/> 中的轮转数组与切换间隔，
    /// 在格口管理器连接成功后按数组顺序循环切换强排口。
    /// </summary>
    public sealed class ChuteForcedRotationHostedService : BackgroundService {
        private static readonly TimeSpan FixedForcedChutePollingInterval = TimeSpan.FromSeconds(1);
        private readonly ILogger<ChuteForcedRotationHostedService> _logger;
        private readonly IChuteManager _chuteManager;
        private readonly ISystemStateManager _systemStateManager;
        private readonly ChuteForcedRotationOptions _options;

        /// <summary>
        /// 初始化格口强排轮转后台服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="chuteManager">格口管理器。</param>
        /// <param name="systemStateManager">系统状态管理器。</param>
        /// <param name="options">强排配置（含轮转与固定强排模式）。</param>
        public ChuteForcedRotationHostedService(
            ILogger<ChuteForcedRotationHostedService> logger,
            IChuteManager chuteManager,
            ISystemStateManager systemStateManager,
            IOptions<ChuteForcedRotationOptions> options) {
            _logger = logger;
            _chuteManager = chuteManager;
            _systemStateManager = systemStateManager;
            _options = options.Value;
        }

        /// <summary>
        /// 执行后台轮转主循环。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            // 步骤1：校验模式与配置合法性，非法配置直接退出并记录日志。
            var rotationEnabled = _options.Enabled;
            var fixedForcedChuteEnabled = _options.ForcedChuteId > 0;
            if (rotationEnabled && fixedForcedChuteEnabled) {
                _logger.LogWarning(
                    "格口强排配置互斥：ForcedRotation.Enabled=true 时将忽略 ForcedChuteId={ForcedChuteId}。",
                    _options.ForcedChuteId);
                fixedForcedChuteEnabled = false;
            }

            if (!rotationEnabled && !fixedForcedChuteEnabled) {
                _logger.LogInformation("格口强排服务已禁用。");
                return;
            }

            var sequence = _options.ChuteSequence.ToArray();
            if (rotationEnabled && sequence.Length == 0) {
                _logger.LogWarning("格口强排轮转模式未配置 ChuteSequence，服务退出。");
                return;
            }

            if (rotationEnabled && _options.SwitchIntervalSeconds < 1) {
                _logger.LogWarning("格口强排轮转后台服务配置非法，SwitchIntervalSeconds 必须大于等于 1。");
                return;
            }

            var index = 0;
            _logger.LogInformation(
                "格口强排服务启动 rotationEnabled={RotationEnabled} forcedChuteId={ForcedChuteId} sequence={Sequence} switchIntervalSeconds={SwitchIntervalSeconds}",
                rotationEnabled,
                _options.ForcedChuteId,
                string.Join(",", sequence),
                _options.SwitchIntervalSeconds);

            await _chuteManager.ConnectAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested) {
                // 步骤2：等待格口管理器进入 Connected 状态，再触发强排切换。
                if (_chuteManager.ConnectionStatus != DeviceConnectionStatus.Connected) {
                    _logger.LogInformation("格口强排轮转等待连接完成 currentStatus={ConnectionStatus}", _chuteManager.ConnectionStatus);
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // 步骤3：根据系统状态与模式计算目标强排口（固定强排口仅在 Running 生效）。
                var targetForcedChuteId = ResolveTargetForcedChuteId(rotationEnabled, fixedForcedChuteEnabled, sequence, ref index);
                if (_chuteManager.ForcedChuteId == targetForcedChuteId) {
                    _logger.LogDebug(
                        "格口强排目标未变化，跳过下发 targetChuteId={TargetChuteId} systemState={SystemState}",
                        targetForcedChuteId,
                        _systemStateManager.CurrentState);
                    if (rotationEnabled) {
                        await Task.Delay(TimeSpan.FromSeconds(_options.SwitchIntervalSeconds), stoppingToken).ConfigureAwait(false);
                    }
                    else {
                        await Task.Delay(FixedForcedChutePollingInterval, stoppingToken).ConfigureAwait(false);
                    }

                    continue;
                }

                var switched = await _chuteManager.SetForcedChuteAsync(targetForcedChuteId, stoppingToken).ConfigureAwait(false);
                if (switched) {
                    _logger.LogInformation(
                        "格口强排切换成功 targetChuteId={TargetChuteId} systemState={SystemState} forcedRotationEnabled={ForcedRotationEnabled}",
                        targetForcedChuteId,
                        _systemStateManager.CurrentState,
                        rotationEnabled);
                }
                else {
                    _logger.LogWarning(
                        "格口强排切换失败 targetChuteId={TargetChuteId} systemState={SystemState} forcedRotationEnabled={ForcedRotationEnabled}",
                        targetForcedChuteId,
                        _systemStateManager.CurrentState,
                        rotationEnabled);
                }

                if (rotationEnabled) {
                    await Task.Delay(TimeSpan.FromSeconds(_options.SwitchIntervalSeconds), stoppingToken).ConfigureAwait(false);
                }
                else {
                    await Task.Delay(FixedForcedChutePollingInterval, stoppingToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// 解析当前应下发的强排格口。
        /// </summary>
        /// <param name="rotationEnabled">是否启用轮转模式。</param>
        /// <param name="fixedForcedChuteEnabled">是否启用固定强排口模式。</param>
        /// <param name="sequence">轮转数组。</param>
        /// <param name="index">当前轮转索引。</param>
        /// <returns>目标强排格口；null 表示关闭强排。</returns>
        private long? ResolveTargetForcedChuteId(bool rotationEnabled, bool fixedForcedChuteEnabled, IReadOnlyList<long> sequence, ref int index) {
            if (rotationEnabled) {
                var chuteId = sequence[index];
                index = (index + 1) % sequence.Count;
                return chuteId;
            }

            if (!fixedForcedChuteEnabled) {
                return null;
            }

            return ShouldEnableFixedForcedChute(_systemStateManager.CurrentState)
                ? _options.ForcedChuteId
                : null;
        }

        /// <summary>
        /// 判断固定强排模式是否允许闭合强排口。
        /// </summary>
        /// <param name="state">当前系统状态。</param>
        /// <returns>允许闭合返回 true，否则 false。</returns>
        private static bool ShouldEnableFixedForcedChute(SystemState state) {
            return state == SystemState.Running;
        }
    }
}
