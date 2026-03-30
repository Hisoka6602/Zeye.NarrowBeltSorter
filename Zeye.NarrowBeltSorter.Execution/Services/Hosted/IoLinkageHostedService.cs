using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zeye.NarrowBeltSorter.Core.Manager.Emc;
using Zeye.NarrowBeltSorter.Core.Manager.IoPanel;
using Zeye.NarrowBeltSorter.Core.Manager.Sensor;
using Zeye.NarrowBeltSorter.Core.Utilities;


namespace Zeye.NarrowBeltSorter.Execution.Services.Hosted {
    /// <summary>
    /// Leadshaine 联动 IO 托管服务（EMC 初始化、点位下发、IoPanel/Sensor 启停编排）。
    /// </summary>
    public sealed class IoLinkageHostedService : BackgroundService {
        private readonly ILogger<IoLinkageHostedService> _logger;
        private readonly IEmcController _emc;
        private readonly IIoPanel _ioPanelManager;
        private readonly ISensorManager _sensorManager;

        /// <summary>
        /// 初始化联动 Io 托管服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="emcController">EMC 控制器。</param>
        /// <param name="ioPanelManager">IoPanel 管理器。</param>
        /// <param name="sensorManager">传感器管理器。</param>
        public IoLinkageHostedService(
            ILogger<IoLinkageHostedService> logger,
            IEmcController emc,
            IIoPanel ioPanelManager,
            ISensorManager sensorManager) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _emc = emc ?? throw new ArgumentNullException(nameof(emc));
            _ioPanelManager = ioPanelManager ?? throw new ArgumentNullException(nameof(ioPanelManager));
            _sensorManager = sensorManager ?? throw new ArgumentNullException(nameof(sensorManager));
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
                _logger.LogError("IoLinkageHostedService 启动失败：EMC 初始化未成功。");
                return;
            }

            // 步骤2：先启动 IoPanel 并下发 IoPanel 点位；Sensor 点位在其启动阶段增量下发。
            await _ioPanelManager.StartMonitoringAsync(stoppingToken).ConfigureAwait(false);
            var monitoredSet = await SensorWorkflowHelper.SyncMonitoredIoPointsToEmcAsync(
                _emc,
                _ioPanelManager.MonitoredPointIds,
                stoppingToken).ConfigureAwait(false);
            if (!monitoredSet) {
                _logger.LogError("IoLinkageHostedService 启动失败：EMC 点位下发未成功。");
                await CleanupAfterStartupFailureAsync(stoppingToken).ConfigureAwait(false);
                return;
            }

            // 步骤3：启动 Sensor 监控模块。
            await _sensorManager.StartMonitoringAsync(stoppingToken).ConfigureAwait(false);
            var monitoredPointCount = _emc.MonitoredIoPoints.Count;
            _logger.LogInformation("IoLinkageHostedService 已启动，监控点数量={PointCount}。", monitoredPointCount);

            // 步骤4：保持服务存活，直至收到停止信号。
            while (!stoppingToken.IsCancellationRequested) {
                await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 停止流程：先停 IoPanel/Sensor，再释放 EMC 控制器。
        /// </summary>
        /// <param name="cancellationToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        public override async Task StopAsync(CancellationToken cancellationToken) {
            await _ioPanelManager.StopMonitoringAsync(cancellationToken).ConfigureAwait(false);
            await _sensorManager.StopMonitoringAsync(cancellationToken).ConfigureAwait(false);
            await _emc.DisposeAsync().ConfigureAwait(false);
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 启动失败后的资源回收流程。
        /// </summary>
        /// <param name="cancellationToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task CleanupAfterStartupFailureAsync(CancellationToken cancellationToken) {
            try {
                await _ioPanelManager.StopMonitoringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "IoLinkageHostedService 启动失败回收异常：停止 IoPanel 失败。");
            }

            try {
                await _sensorManager.StopMonitoringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "IoLinkageHostedService 启动失败回收异常：停止 Sensor 失败。");
            }

            try {
                await _emc.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "IoLinkageHostedService 启动失败回收异常：释放 EMC 失败。");
            }
        }
    }
}
