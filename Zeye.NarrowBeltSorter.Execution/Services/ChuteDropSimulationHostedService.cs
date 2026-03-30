using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Execution.Parcel;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Options.Chutes.Zeye.NarrowBeltSorter.Core.Options.Chutes;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 包裹落格模拟托管服务：在系统运行态下，按配置策略自动给新包裹分配目标格口。
    /// </summary>
    public sealed class ChuteDropSimulationHostedService : BackgroundService {
        private readonly ILogger<ChuteDropSimulationHostedService> _logger;
        private readonly IParcelManager _parcelManager;
        private readonly ISystemStateManager _systemStateManager;
        private readonly SafeExecutor _safeExecutor;
        private readonly ChuteDropSimulationOptions _options;
        private readonly object _roundRobinSync = new();
        private EventHandler<ParcelCreatedEventArgs>? _parcelCreatedHandler;
        private int _roundRobinIndex;

        public ChuteDropSimulationHostedService(
            ILogger<ChuteDropSimulationHostedService> logger,
            IParcelManager parcelManager,

            ISystemStateManager systemStateManager,
            SafeExecutor safeExecutor,
        IOptions<ChuteDropSimulationOptions> options) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _parcelManager = parcelManager ?? throw new ArgumentNullException(nameof(parcelManager));
            _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            if (!_options.Enabled) {
                _logger.LogInformation("包裹落格模拟托管服务已禁用。");
                return;
            }

            if (_options.AssignDelayMs < 0) {
                _logger.LogWarning("包裹落格模拟配置非法：AssignDelayMs 必须大于等于 0。当前值={AssignDelayMs}", _options.AssignDelayMs);
                return;
            }

            if (!TryValidateMode(out var normalizedMode)) {
                return;
            }

            _parcelCreatedHandler = (_, args) => {
                _ = HandleParcelCreatedAsync(args, normalizedMode, stoppingToken);
            };

            _parcelManager.ParcelCreated += _parcelCreatedHandler;
            _logger.LogInformation(
                "包裹落格模拟托管服务已启动 mode={Mode} assignDelayMs={AssignDelayMs} fixedChuteId={FixedChuteId} sequenceCount={SequenceCount}",
                normalizedMode,
                _options.AssignDelayMs,
                _options.FixedChuteId,
                _options.ChuteSequence.Count);

            try {
                while (!stoppingToken.IsCancellationRequested) {
                    await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken).ConfigureAwait(false);
                }
            }
            finally {
                Unsubscribe();
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken) {
            Unsubscribe();
            return base.StopAsync(cancellationToken);
        }

        /// <summary>
        /// 取消订阅包裹创建事件，避免服务停止后仍接收事件。
        /// </summary>
        private void Unsubscribe() {
            if (_parcelCreatedHandler is null) {
                return;
            }

            _parcelManager.ParcelCreated -= _parcelCreatedHandler;
            _parcelCreatedHandler = null;
        }

        /// <summary>
        /// 校验并标准化模拟分配模式。
        /// </summary>
        /// <param name="normalizedMode">标准化后的模式字符串。</param>
        /// <returns>模式是否有效。</returns>
        private bool TryValidateMode(out string normalizedMode) {
            normalizedMode = _options.Mode.Trim();
            if (normalizedMode.Equals("Fixed", StringComparison.OrdinalIgnoreCase)) {
                normalizedMode = "Fixed";
                if (_options.FixedChuteId <= 0) {
                    _logger.LogWarning("包裹落格模拟配置非法：Mode=Fixed 时 FixedChuteId 必须为正数。当前值={FixedChuteId}", _options.FixedChuteId);
                    return false;
                }

                return true;
            }

            if (normalizedMode.Equals("RoundRobin", StringComparison.OrdinalIgnoreCase)) {
                normalizedMode = "RoundRobin";
                if (_options.ChuteSequence.Count == 0 || _options.ChuteSequence.Any(chuteId => chuteId <= 0)) {
                    _logger.LogWarning("包裹落格模拟配置非法：Mode=RoundRobin 时 ChuteSequence 必须为正数数组且不能为空。");
                    return false;
                }

                return true;
            }

            _logger.LogWarning("包裹落格模拟配置非法：Mode 仅支持 Fixed/RoundRobin。当前值={Mode}", _options.Mode);
            return false;
        }

        /// <summary>
        /// 处理包裹创建事件并尝试分配目标格口。
        /// </summary>
        /// <param name="args">包裹创建事件参数。</param>
        /// <param name="mode">已标准化的分配模式。</param>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task HandleParcelCreatedAsync(ParcelCreatedEventArgs args, string mode, CancellationToken stoppingToken) {
            // 步骤1：系统不在运行态时直接跳过分配，避免非运行窗口写入目标格口。
            if (_systemStateManager.CurrentState != SystemState.Running) {
                _logger.LogInformation(
                    "包裹落格模拟跳过 ParcelId={ParcelId} 原因=系统不在Running",
                    args.ParcelId);
                return;
            }

            // 步骤2：按当前模式解析目标格口，解析失败时记录可判因日志并返回。
            if (!TryResolveTargetChute(mode, out var targetChuteId)) {
                _logger.LogWarning(
                    "包裹落格模拟分配失败 ParcelId={ParcelId} 原因=无法解析目标格口 mode={Mode}",
                    args.ParcelId,
                    mode);
                return;
            }

            // 步骤3：在统一安全执行器中完成延迟等待、二次状态校验与目标格口分配。
            await _safeExecutor.ExecuteAsync(
                async token => {
                    if (_options.AssignDelayMs > 0) {
                        await Task.Delay(_options.AssignDelayMs, token).ConfigureAwait(false);
                    }

                    if (_systemStateManager.CurrentState != SystemState.Running) {
                        _logger.LogInformation("包裹落格模拟跳过 ParcelId={ParcelId} 原因=延迟后系统不在Running", args.ParcelId);
                        return;
                    }

                    var assigned = await _parcelManager.AssignTargetChuteAsync(
                        args.ParcelId,
                        targetChuteId,
                        DateTime.Now,
                        token).ConfigureAwait(false);
                    if (!assigned) {
                        _logger.LogWarning("包裹落格模拟分配失败：parcelId={ParcelId} targetChuteId={TargetChuteId}", args.ParcelId, targetChuteId);
                        return;
                    }

                    _logger.LogInformation("包裹落格模拟分配成功：parcelId={ParcelId} targetChuteId={TargetChuteId}", args.ParcelId, targetChuteId);
                },
                "ChuteDropSimulationHostedService.AssignTargetChute",
                stoppingToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 根据当前模式解析目标格口编号。
        /// </summary>
        /// <param name="mode">分配模式。</param>
        /// <param name="targetChuteId">解析得到的目标格口编号。</param>
        /// <returns>是否解析成功。</returns>
        private bool TryResolveTargetChute(string mode, out long targetChuteId) {
            if (mode == "Fixed") {
                targetChuteId = _options.FixedChuteId;
                return true;
            }

            if (mode == "RoundRobin") {
                lock (_roundRobinSync) {
                    targetChuteId = _options.ChuteSequence[_roundRobinIndex];
                    _roundRobinIndex = (_roundRobinIndex + 1) % _options.ChuteSequence.Count;
                    return true;
                }
            }

            targetChuteId = 0;
            return false;
        }
    }
}
