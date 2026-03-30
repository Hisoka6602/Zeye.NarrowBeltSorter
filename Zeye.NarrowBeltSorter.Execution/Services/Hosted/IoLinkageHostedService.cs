using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Channels;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.System;
using Zeye.NarrowBeltSorter.Core.Manager.Emc;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;

namespace Zeye.NarrowBeltSorter.Execution.Services.Hosted {
    /// <summary>
    /// Leadshaine 联动 IO 托管服务（系统状态 -> 输出点位）。
    /// </summary>
    public sealed class IoLinkageHostedService : BackgroundService {
        private readonly ILogger<IoLinkageHostedService> _logger;
        private readonly ISystemStateManager _systemStateManager;
        private readonly IEmcController _emcController;
        private readonly IReadOnlyList<LeadshaineIoLinkagePointOptions> _rules;
        private EventHandler<StateChangeEventArgs>? _stateChangedHandler;
        private Channel<SystemState>? _stateChannel;

        /// <summary>
        /// 初始化联动 IO 托管服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="systemStateManager">系统状态管理器。</param>
        /// <param name="emcController">EMC 控制器。</param>
        /// <param name="options">联动配置。</param>
        public IoLinkageHostedService(
            ILogger<IoLinkageHostedService> logger,
            ISystemStateManager systemStateManager,
            IEmcController emcController,
            IOptions<LeadshaineIoLinkageOptions> options) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));
            _emcController = emcController ?? throw new ArgumentNullException(nameof(emcController));
            _rules = options?.Value?.Points ?? [];
        }

        /// <summary>
        /// 执行联动主循环：订阅系统状态变化并驱动 IO 写入。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            _stateChannel = Channel.CreateUnbounded<SystemState>(new UnboundedChannelOptions {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

            _stateChangedHandler = (_, args) => {
                try {
                    _stateChannel.Writer.TryWrite(args.NewState);
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "IoLinkageHostedService 投递系统状态失败。");
                }
            };
            _systemStateManager.StateChanged += _stateChangedHandler;

            while (!stoppingToken.IsCancellationRequested) {
                try {
                    var newState = await _stateChannel.Reader.ReadAsync(stoppingToken).ConfigureAwait(false);
                    await HandleStateChangedAsync(newState, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    break;
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "IoLinkageHostedService 主循环执行异常。");
                }
            }
        }

        /// <summary>
        /// 停止服务并卸载系统状态订阅。
        /// </summary>
        /// <param name="cancellationToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        public override async Task StopAsync(CancellationToken cancellationToken) {
            try {
                if (_stateChangedHandler != null) {
                    _systemStateManager.StateChanged -= _stateChangedHandler;
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "IoLinkageHostedService 卸载系统状态订阅失败。");
            }

            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 根据系统状态触发联动规则。
        /// 规则按配置顺序串行执行，保证同一状态下多规则的确定性先后次序。
        /// </summary>
        /// <param name="newState">新状态。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task HandleStateChangedAsync(SystemState newState, CancellationToken cancellationToken) {
            foreach (var rule in _rules) {
                if (rule.RelatedSystemState != newState) {
                    continue;
                }

                if (rule.DelayMs > 0) {
                    try {
                        await Task.Delay(TimeSpan.FromMilliseconds(rule.DelayMs), cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) {
                        return;
                    }
                }

                try {
                    await _emcController.WriteIoAsync(rule.PointId, rule.TriggerValue, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "IoLinkageHostedService 联动写入失败：PointId={PointId}。", rule.PointId);
                    continue;
                }

                if (rule.DurationMs <= 0) {
                    continue;
                }

                try {
                    await Task.Delay(TimeSpan.FromMilliseconds(rule.DurationMs), cancellationToken).ConfigureAwait(false);
                    await _emcController.WriteIoAsync(rule.PointId, !rule.TriggerValue, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    return;
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "IoLinkageHostedService 联动回写失败：PointId={PointId}。", rule.PointId);
                }
            }
        }
    }
}
