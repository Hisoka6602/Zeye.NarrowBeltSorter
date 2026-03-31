using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Manager.Emc;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.System;
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
        private readonly object _stateSync = new();
        private readonly SemaphoreSlim _stateSignal = new(0, 1);
        private EventHandler<StateChangeEventArgs>? _stateChangedHandler;
        private SystemState _pendingState;
        private bool _hasPendingState;

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
            _stateChangedHandler = OnSystemStateChanged;
            _systemStateManager.StateChanged += _stateChangedHandler;

            try {
                while (!stoppingToken.IsCancellationRequested) {
                    try {
                        await _stateSignal.WaitAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) {
                        break;
                    }

                    SystemState newState;
                    lock (_stateSync) {
                        if (!_hasPendingState) {
                            continue;
                        }

                        newState = _pendingState;
                        _hasPendingState = false;
                    }

                    try {
                        await HandleStateChangedAsync(newState, stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) {
                        _logger.LogError(ex, "IoLinkageHostedService 主循环执行异常。");
                    }
                }
            }
            finally {
                TryUnsubscribeStateChanged();
            }
        }

        /// <summary>
        /// 停止服务并卸载系统状态订阅。
        /// </summary>
        /// <param name="cancellationToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        public override async Task StopAsync(CancellationToken cancellationToken) {
            TryUnsubscribeStateChanged();

            lock (_stateSync) {
                TryReleaseStateSignal();
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
                if (!ShouldTriggerRule(rule.RelatedSystemState, newState)) {
                    continue;
                }

                // 步骤1：命中规则后先执行延迟窗口，保障配置中的延时语义。
                if (rule.DelayMs > 0) {
                    try {
                        await Task.Delay(TimeSpan.FromMilliseconds(rule.DelayMs), cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) {
                        return;
                    }
                }

                // 步骤2：执行触发电平写入，写入失败仅记录日志并继续处理后续规则。
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

                // 步骤3：当配置了保持时长时，等待后回写反向电平，形成脉冲动作闭环。
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

        /// <summary>
        /// 判断规则是否应在当前系统状态下触发。
        /// 约定：急停状态下也应执行“停止态（Paused）”联动，保证停机相关 IO 在急停时同样生效。
        /// </summary>
        /// <param name="ruleState">规则配置的目标状态。</param>
        /// <param name="currentState">当前系统状态。</param>
        /// <returns>命中返回 true，否则 false。</returns>
        private static bool ShouldTriggerRule(SystemState ruleState, SystemState currentState) {
            if (ruleState == currentState) {
                return true;
            }

            return currentState == SystemState.EmergencyStop && ruleState == SystemState.Paused;
        }

        /// <summary>
        /// 处理系统状态变更并仅保留最新待处理状态。
        /// </summary>
        /// <param name="sender">事件发送方。</param>
        /// <param name="args">状态变更参数。</param>
        private void OnSystemStateChanged(object? sender, StateChangeEventArgs args) {
            try {
                lock (_stateSync) {
                    var shouldSignal = !_hasPendingState;
                    _pendingState = args.NewState;
                    _hasPendingState = true;
                    if (shouldSignal) {
                        TryReleaseStateSignal();
                    }
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "IoLinkageHostedService 投递系统状态失败。");
            }
        }

        /// <summary>
        /// 尝试卸载系统状态订阅。
        /// </summary>
        private void TryUnsubscribeStateChanged() {
            try {
                if (_stateChangedHandler != null) {
                    _systemStateManager.StateChanged -= _stateChangedHandler;
                    _stateChangedHandler = null;
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "IoLinkageHostedService 卸载系统状态订阅失败。");
            }
        }

        /// <summary>
        /// 尝试释放状态信号量，保证高并发场景不会因重复释放导致异常中断。
        /// </summary>
        private void TryReleaseStateSignal() {
            try {
                if (_stateSignal.CurrentCount == 0) {
                    _stateSignal.Release();
                }
            }
            catch (SemaphoreFullException ex) {
                _logger.LogDebug(ex, "IoLinkageHostedService 状态信号量已满，忽略重复释放。");
            }
        }
        /// <summary>
        /// 释放服务资源，包括 SemaphoreSlim。
        /// </summary>
        public override void Dispose() {
            _stateSignal.Dispose();
            base.Dispose();
        }
    }
}
