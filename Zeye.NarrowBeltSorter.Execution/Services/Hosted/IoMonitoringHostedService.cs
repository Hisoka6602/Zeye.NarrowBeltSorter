using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zeye.NarrowBeltSorter.Core.Manager.Emc;
using Zeye.NarrowBeltSorter.Core.Manager.IoPanel;
using Zeye.NarrowBeltSorter.Core.Manager.Sensor;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Utilities;
namespace Zeye.NarrowBeltSorter.Execution.Services.Hosted {
    /// <summary>
    /// Leadshaine Io 监控托管服务（EMC 初始化、点位下发、IoPanel/Sensor 启停编排）。
    /// </summary>
    public sealed class IoMonitoringHostedService : BackgroundService {
        private readonly ILogger<IoMonitoringHostedService> _logger;
        private readonly IEmcController _emc;
        private readonly IIoPanel _ioPanelManager;
        private readonly ISensorManager _sensorManager;
        private readonly LeadshaineIoPointBindingCollectionOptions _pointOptions;

        /// <summary>
        /// 初始化 IO 监控托管服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="emcController">EMC 控制器。</param>
        /// <param name="ioPanelManager">IoPanel 管理器。</param>
        /// <param name="sensorManager">传感器管理器。</param>
        public IoMonitoringHostedService(
            ILogger<IoMonitoringHostedService> logger,
            IEmcController emc,
            IIoPanel ioPanelManager,
            ISensorManager sensorManager,
            LeadshaineIoPointBindingCollectionOptions pointOptions) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _emc = emc ?? throw new ArgumentNullException(nameof(emc));
            _ioPanelManager = ioPanelManager ?? throw new ArgumentNullException(nameof(ioPanelManager));
            _sensorManager = sensorManager ?? throw new ArgumentNullException(nameof(sensorManager));
            _pointOptions = pointOptions ?? throw new ArgumentNullException(nameof(pointOptions));
        }

        /// <summary>
        /// 启动主流程：EMC 初始化 -> 点位下发 -> IoPanel/Sensor 启动。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>执行任务。</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            // 步骤1：初始化 EMC，失败则终止本服务启动。
            var initialized = await _emc.InitializeAsync(stoppingToken).ConfigureAwait(false);
            if (!initialized) {
                _logger.LogError("IoMonitoringHostedService 启动失败：EMC 初始化未成功。");
                return;
            }

            // 步骤2：先启动 IoPanel，并将“全部输入点 + IoPanel 点位”一次性下发，避免仅部分点位被监控。
            await _ioPanelManager.StartMonitoringAsync(stoppingToken).ConfigureAwait(false);
            var allInputPointIds = _pointOptions.Points
                .Where(static point =>
                    !string.IsNullOrWhiteSpace(point.PointId)
                    && string.Equals(point.Binding.Area, "Input", StringComparison.OrdinalIgnoreCase))
                .Select(static point => point.PointId)
                .ToArray();
            var bootstrapPointIds = allInputPointIds
                .Concat(_ioPanelManager.MonitoredPointIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var monitoredSet = await SensorWorkflowHelper.SyncMonitoredIoPointsToEmcAsync(
                _emc,
                bootstrapPointIds,
                stoppingToken).ConfigureAwait(false);
            if (!monitoredSet) {
                _logger.LogError("IoMonitoringHostedService 启动失败：EMC 点位下发未成功。");
                await CleanupAfterStartupFailureAsync(stoppingToken).ConfigureAwait(false);
                return;
            }

            // 步骤3：启动 Sensor 监控模块。
            await _sensorManager.StartMonitoringAsync(stoppingToken).ConfigureAwait(false);
            var monitoredPointCount = _emc.MonitoredIoPoints.Count;
            _logger.LogInformation("IoMonitoringHostedService 已启动，监控点数量={PointCount}。", monitoredPointCount);

            // 步骤4：保持服务存活，直至收到停止信号。
            try {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // 宿主正常停止，退出保活等待。
            }
        }

        /// <summary>
        /// 停止流程：先停 IoPanel/Sensor，再释放 EMC 控制器。
        /// 各步骤独立 try-catch，确保单步异常不中断后续资源释放。
        /// </summary>
        /// <param name="cancellationToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        public override async Task StopAsync(CancellationToken cancellationToken) {
            await TryReleaseResourcesAsync(cancellationToken, "停止").ConfigureAwait(false);
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 启动失败后的资源回收流程。
        /// </summary>
        /// <param name="cancellationToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        private Task CleanupAfterStartupFailureAsync(CancellationToken cancellationToken) {
            return TryReleaseResourcesAsync(cancellationToken, "启动失败回收");
        }

        /// <summary>
        /// 三段式资源释放：停 IoPanel → 停 Sensor → Dispose EMC。
        /// 每步独立 catch 保证后续步骤不受阻断；<see cref="OperationCanceledException"/> 降级为 Debug 日志（宿主正常停止）。
        /// </summary>
        /// <param name="cancellationToken">停止令牌（用于判断是否为预期取消）。</param>
        /// <param name="context">上下文描述（用于日志前缀，如"停止"或"启动失败回收"）。</param>
        /// <returns>异步任务。</returns>
        private async Task TryReleaseResourcesAsync(CancellationToken cancellationToken, string context) {
            // 步骤1：停止 IoPanel 监控。
            try {
                await _ioPanelManager.StopMonitoringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                _logger.LogDebug("IoMonitoringHostedService {Context}：停止 IoPanel 因令牌取消而中断（预期行为）。", context);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "IoMonitoringHostedService {Context} 异常：停止 IoPanel 失败。", context);
            }

            // 步骤2：停止 Sensor 监控。
            try {
                await _sensorManager.StopMonitoringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                _logger.LogDebug("IoMonitoringHostedService {Context}：停止 Sensor 因令牌取消而中断（预期行为）。", context);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "IoMonitoringHostedService {Context} 异常：停止 Sensor 失败。", context);
            }

            // 步骤3：释放 EMC 控制器。
            try {
                await _emc.DisposeAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                _logger.LogDebug("IoMonitoringHostedService {Context}：释放 EMC 因令牌取消而中断（预期行为）。", context);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "IoMonitoringHostedService {Context} 异常：释放 EMC 失败。", context);
            }
        }
    }
}
