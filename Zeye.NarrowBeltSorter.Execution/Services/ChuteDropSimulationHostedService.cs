using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;
using Zeye.NarrowBeltSorter.Core.Utilities;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 包裹落格模拟托管服务：在系统运行态下，按配置策略自动给新包裹分配目标格口。
    /// </summary>
    public sealed class ChuteDropSimulationHostedService : BackgroundService {
        private readonly ILogger<ChuteDropSimulationHostedService> _logger;
        private readonly IParcelManager _parcelManager;
        private readonly ISystemStateManager _systemStateManager;
        private readonly SafeExecutor _safeExecutor;
        private readonly IOptionsMonitor<ChuteDropSimulationOptions> _optionsMonitor;
        private readonly IDisposable _optionsChangedRegistration;
        private readonly object _roundRobinSync = new();
        private readonly object _modeValidationLogSync = new();
        private EventHandler<ParcelCreatedEventArgs>? _parcelCreatedHandler;
        private ChuteDropSimulationOptions _currentOptions;
        private int _roundRobinIndex;
        private string? _lastInvalidModeSignature;

        /// <summary>
        /// 初始化包裹落格模拟托管服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="parcelManager">包裹管理器。</param>
        /// <param name="systemStateManager">系统状态管理器。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <param name="optionsMonitor">落格模拟配置监听器。</param>
        public ChuteDropSimulationHostedService(
            ILogger<ChuteDropSimulationHostedService> logger,
            IParcelManager parcelManager,
            ISystemStateManager systemStateManager,
            SafeExecutor safeExecutor,
            IOptionsMonitor<ChuteDropSimulationOptions> optionsMonitor) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _parcelManager = parcelManager ?? throw new ArgumentNullException(nameof(parcelManager));
            _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _currentOptions = _optionsMonitor.CurrentValue ?? throw new InvalidOperationException("ChuteDropSimulationOptions 不能为空。");
            _optionsChangedRegistration = _optionsMonitor.OnChange(RefreshOptionsSnapshot) ?? throw new InvalidOperationException("ChuteDropSimulationOptions.OnChange 订阅失败。");
        }

        /// <summary>
        /// 当前落格模拟配置快照。
        /// </summary>
        private ChuteDropSimulationOptions CurrentOptions => Volatile.Read(ref _currentOptions);

        /// <summary>
        /// 执行后台主循环，监听包裹创建并按配置分配目标格口。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            var options = CurrentOptions;
            if (!options.Enabled) {
                _logger.LogInformation("包裹落格模拟托管服务已禁用。");
                return;
            }

            if (options.AssignDelayMs < 0) {
                _logger.LogWarning("包裹落格模拟配置非法：AssignDelayMs 必须大于等于 0。当前值={AssignDelayMs}", options.AssignDelayMs);
                return;
            }

            if (!TryValidateMode(options, out var normalizedMode)) {
                return;
            }

            _parcelCreatedHandler = (_, args) => {
                _ = HandleParcelCreatedAsync(args, stoppingToken);
            };

            _parcelManager.ParcelCreated += _parcelCreatedHandler;
            _logger.LogInformation(
                "包裹落格模拟托管服务已启动 mode={Mode} assignDelayMs={AssignDelayMs} fixedChuteId={FixedChuteId} sequenceCount={SequenceCount}",
                normalizedMode,
                options.AssignDelayMs,
                options.FixedChuteId,
                options.ChuteSequence.Count);

            try {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                _logger.LogDebug("包裹落格模拟托管服务已正常停止。");
            }
            finally {
                Unsubscribe();
            }
        }

        /// <summary>
        /// 停止服务时取消订阅包裹事件。
        /// </summary>
        /// <param name="cancellationToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
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
        /// <param name="options">落格模拟配置。</param>
        /// <param name="normalizedMode">标准化后的模式字符串。</param>
        /// <param name="parcelId">包裹编号（可空，仅用于日志）。</param>
        /// <returns>模式是否有效。</returns>
        private bool TryValidateMode(ChuteDropSimulationOptions options, out string normalizedMode, long? parcelId = null) {
            // 步骤1：生成配置签名，供非法配置日志节流使用。
            var signature = BuildModeSignature(options);
            // 步骤2：先标准化模式名称，再按模式执行参数校验。
            normalizedMode = options.Mode.Trim();
            if (normalizedMode.Equals("Fixed", StringComparison.OrdinalIgnoreCase)) {
                normalizedMode = "Fixed";
                if (options.FixedChuteId <= 0) {
                    LogInvalidModeWarning(
                        signature,
                        parcelId,
                        "包裹落格模拟配置非法：Mode=Fixed 时 FixedChuteId 必须为正数。当前值={FixedChuteId}",
                        options.FixedChuteId);
                    return false;
                }

                ResetInvalidModeWarning(signature);
                return true;
            }

            if (normalizedMode.Equals("RoundRobin", StringComparison.OrdinalIgnoreCase)) {
                normalizedMode = "RoundRobin";
                if (options.ChuteSequence.Count == 0 || options.ChuteSequence.Any(chuteId => chuteId <= 0)) {
                    LogInvalidModeWarning(
                        signature,
                        parcelId,
                        "包裹落格模拟配置非法：Mode=RoundRobin 时 ChuteSequence 必须为正数数组且不能为空。");
                    return false;
                }

                ResetInvalidModeWarning(signature);
                return true;
            }

            // 步骤3：未知模式按非法配置记录并返回失败。
            LogInvalidModeWarning(
                signature,
                parcelId,
                "包裹落格模拟配置非法：Mode 仅支持 Fixed/RoundRobin。当前值={Mode}",
                options.Mode);
            return false;
        }

        /// <summary>
        /// 处理包裹创建事件并尝试分配目标格口。
        /// </summary>
        /// <param name="args">包裹创建事件参数。</param>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task HandleParcelCreatedAsync(ParcelCreatedEventArgs args, CancellationToken stoppingToken) {
            // 步骤1：系统不在运行态时直接跳过分配，避免非运行窗口写入目标格口。
            if (_systemStateManager.CurrentState != SystemState.Running) {
                _logger.LogInformation(
                    "包裹落格模拟跳过 ParcelId={ParcelId} 原因=系统不在Running",
                    args.ParcelId);
                return;
            }

            // 步骤2：在统一安全执行器中完成延迟等待、二次状态校验与按最新配置解析目标格口。
            await _safeExecutor.ExecuteAsync(
                async token => {
                    var latestOptions = CurrentOptions;
                    if (latestOptions.AssignDelayMs < 0) {
                        _logger.LogWarning("包裹落格模拟分配跳过 ParcelId={ParcelId} 原因=AssignDelayMs 非法", args.ParcelId);
                        return;
                    }

                    if (latestOptions.AssignDelayMs > 0) {
                        await Task.Delay(latestOptions.AssignDelayMs, token).ConfigureAwait(false);
                    }

                    if (_systemStateManager.CurrentState != SystemState.Running) {
                        _logger.LogInformation("包裹落格模拟跳过 ParcelId={ParcelId} 原因=延迟后系统不在Running", args.ParcelId);
                        return;
                    }

                    if (!TryValidateMode(latestOptions, out var mode, args.ParcelId)) {
                        return;
                    }

                    if (!TryResolveTargetChute(latestOptions, mode, out var targetChuteId)) {
                        _logger.LogWarning(
                            "包裹落格模拟分配失败 ParcelId={ParcelId} 原因=无法解析目标格口 mode={Mode}",
                            args.ParcelId,
                            mode);
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
        /// 刷新落格模拟配置快照。
        /// </summary>
        /// <param name="options">最新落格模拟配置。</param>
        private void RefreshOptionsSnapshot(ChuteDropSimulationOptions options) {
            Volatile.Write(ref _currentOptions, options);
        }

        /// <summary>
        /// 根据当前模式解析目标格口编号。
        /// </summary>
        /// <param name="options">落格模拟配置。</param>
        /// <param name="mode">分配模式。</param>
        /// <param name="targetChuteId">解析得到的目标格口编号。</param>
        /// <returns>是否解析成功。</returns>
        private bool TryResolveTargetChute(ChuteDropSimulationOptions options, string mode, out long targetChuteId) {
            if (mode == "Fixed") {
                targetChuteId = options.FixedChuteId;
                return true;
            }

            if (mode == "RoundRobin") {
                lock (_roundRobinSync) {
                    if (options.ChuteSequence.Count == 0) {
                        targetChuteId = 0;
                        return false;
                    }

                    if (_roundRobinIndex >= options.ChuteSequence.Count) {
                        _roundRobinIndex = 0;
                    }

                    targetChuteId = options.ChuteSequence[_roundRobinIndex];
                    _roundRobinIndex = (_roundRobinIndex + 1) % options.ChuteSequence.Count;
                    return true;
                }
            }

            targetChuteId = 0;
            return false;
        }

        /// <summary>
        /// 构建模式配置签名，用于非法配置日志节流。
        /// </summary>
        /// <param name="options">落格模拟配置。</param>
        /// <returns>签名字符串。</returns>
        private static string BuildModeSignature(ChuteDropSimulationOptions options) {
            return string.Join(
                "|",
                options.Mode,
                options.FixedChuteId,
                options.AssignDelayMs,
                string.Join(",", options.ChuteSequence));
        }

        /// <summary>
        /// 记录非法模式告警，并对同一签名重复告警做节流。
        /// </summary>
        /// <param name="signature">配置签名。</param>
        /// <param name="parcelId">包裹标识。</param>
        /// <param name="messageTemplate">日志模板。</param>
        /// <param name="args">日志参数。</param>
        private void LogInvalidModeWarning(string signature, long? parcelId, string messageTemplate, params object?[] args) {
            var shouldLog = false;
            lock (_modeValidationLogSync) {
                if (!string.Equals(_lastInvalidModeSignature, signature, StringComparison.Ordinal)) {
                    _lastInvalidModeSignature = signature;
                    shouldLog = true;
                }
            }

            if (!shouldLog) {
                return;
            }

            var suffix = parcelId.HasValue
                ? $" parcelId={parcelId.Value}"
                : string.Empty;
            var enrichedTemplate = string.Concat(messageTemplate, "（后续重复已节流）{Suffix}");
            var enrichedArgs = new object?[args.Length + 1];
            args.CopyTo(enrichedArgs, 0);
            enrichedArgs[^1] = suffix;
            _logger.LogWarning(enrichedTemplate, enrichedArgs);
        }

        /// <summary>
        /// 清除非法配置节流状态，便于后续再次非法时重新告警。
        /// </summary>
        /// <param name="signature">当前配置签名。</param>
        private void ResetInvalidModeWarning(string signature) {
            lock (_modeValidationLogSync) {
                if (string.Equals(_lastInvalidModeSignature, signature, StringComparison.Ordinal)) {
                    _lastInvalidModeSignature = null;
                }
            }
        }

        /// <summary>
        /// 释放配置热更新订阅资源。
        /// </summary>
        public override void Dispose() {
            _optionsChangedRegistration.Dispose();
            base.Dispose();
        }
    }
}
