using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Enums.Device;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;

namespace Zeye.NarrowBeltSorter.Host.Services {

    /// <summary>
    /// 格口强排轮转后台服务。
    /// 依赖 <see cref="ChuteForcedRotationOptions"/> 中的轮转数组与切换间隔，
    /// 在格口管理器连接成功后按数组顺序循环切换强排口。
    /// </summary>
    public sealed class ChuteForcedRotationService : BackgroundService {
        private readonly ILogger<ChuteForcedRotationService> _logger;
        private readonly IChuteManager _chuteManager;
        private readonly ChuteForcedRotationOptions _options;

        /// <summary>
        /// 初始化格口强排轮转后台服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="chuteManager">格口管理器。</param>
        /// <param name="options">轮转配置。</param>
        public ChuteForcedRotationService(
            ILogger<ChuteForcedRotationService> logger,
            IChuteManager chuteManager,
            IOptions<ChuteForcedRotationOptions> options) {
            _logger = logger;
            _chuteManager = chuteManager;
            _options = options.Value;
        }

        /// <summary>
        /// 执行后台轮转主循环。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            // 步骤1：校验开关与配置合法性，非法配置直接退出并记录日志。
            if (!_options.Enabled) {
                _logger.LogInformation("格口强排轮转后台服务已禁用。");
                return;
            }

            if (_options.ChuteSequence.Count == 0) {
                _logger.LogWarning("格口强排轮转后台服务未配置 ChuteSequence，服务退出。");
                return;
            }

            if (_options.SwitchIntervalSeconds < 1) {
                _logger.LogWarning("格口强排轮转后台服务配置非法，SwitchIntervalSeconds 必须大于等于 1。");
                return;
            }

            var sequence = _options.ChuteSequence.ToArray();
            var index = 0;
            _logger.LogInformation(
                "格口强排轮转后台服务启动 sequence={Sequence} switchIntervalSeconds={SwitchIntervalSeconds}",
                string.Join(",", sequence),
                _options.SwitchIntervalSeconds);

            await _chuteManager.ConnectAsync(stoppingToken);
            await ApplyInfraredOptionsPerChuteAsync(stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested) {
                // 步骤2：等待格口管理器进入 Connected 状态，再触发强排切换。
                if (_chuteManager.ConnectionStatus != DeviceConnectionStatus.Connected) {
                    _logger.LogInformation("格口强排轮转等待连接完成 currentStatus={ConnectionStatus}", _chuteManager.ConnectionStatus);
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // 步骤3：按数组索引执行强排并推进索引，形成循环轮转。
                var chuteId = sequence[index];
                var switched = await _chuteManager.SetForcedChuteAsync(chuteId, stoppingToken).ConfigureAwait(false);
                if (switched) {
                    _logger.LogInformation("格口强排轮转成功 chuteId={ChuteId} index={Index}", chuteId, index);
                }
                else {
                    _logger.LogWarning("格口强排轮转失败 chuteId={ChuteId} index={Index}", chuteId, index);
                }

                index = (index + 1) % sequence.Length;
                await Task.Delay(TimeSpan.FromSeconds(_options.SwitchIntervalSeconds), stoppingToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 在格口创建完成后，为每个格口下发自身红外参数。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task ApplyInfraredOptionsPerChuteAsync(CancellationToken stoppingToken) {
            // 步骤1：遍历当前管理器中的全部格口。
            foreach (var chute in _chuteManager.Chutes) {
                // 步骤2：每个格口调用自身 SetInfraredChuteOptionsAsync 下发红外参数。
                var applied = await chute
                    .SetInfraredChuteOptionsAsync(
                        chute.InfraredChuteOptions,
                        "格口轮转服务初始化",
                        stoppingToken)
                    .ConfigureAwait(false);
                // 步骤3：按结果记录下发日志，便于初始化排障。
                if (applied) {
                    _logger.LogInformation("格口初始化红外参数成功 chuteId={ChuteId}", chute.Id);
                }
                else {
                    _logger.LogWarning("格口初始化红外参数失败 chuteId={ChuteId}", chute.Id);
                }
            }
        }
    }
}
