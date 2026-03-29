using Zeye.NarrowBeltSorter.Core.Manager.Emc;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.IoPanel;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Sensor;
using Microsoft.Extensions.Options;

namespace Zeye.NarrowBeltSorter.Host.Services.Hosted {
    /// <summary>
    /// Leadshaine IO 监控托管服务（EMC 初始化、点位下发、IoPanel/Sensor 启停编排）。
    /// </summary>
    public sealed class IoMonitoringHostedService : BackgroundService {
        private readonly ILogger<IoMonitoringHostedService> _logger;
        private readonly IEmcController _emc;
        private readonly LeadshaineIoPanelManager _ioPanelManager;
        private readonly LeadshaineSensorManager _sensorManager;
        private readonly LeadshaineIoPanelButtonBindingCollectionOptions _ioPanelOptions;
        private readonly LeadshaineSensorBindingCollectionOptions _sensorOptions;

        /// <summary>
        /// 初始化 Io 监控托管服务。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        /// <param name="emcController">EMC 控制器。</param>
        /// <param name="ioPanelManager">IoPanel 管理器。</param>
        /// <param name="sensorManager">传感器管理器。</param>
        /// <param name="ioPanelOptions">IoPanel 绑定配置。</param>
        /// <param name="sensorOptions">传感器绑定配置。</param>
        public IoMonitoringHostedService(
            ILogger<IoMonitoringHostedService> logger,
            IEmcController emc,
            LeadshaineIoPanelManager ioPanelManager,
            LeadshaineSensorManager sensorManager,
            IOptions<LeadshaineIoPanelButtonBindingCollectionOptions> ioPanelOptions,
            IOptions<LeadshaineSensorBindingCollectionOptions> sensorOptions) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _emc = emc ?? throw new ArgumentNullException(nameof(emc));
            _ioPanelManager = ioPanelManager ?? throw new ArgumentNullException(nameof(ioPanelManager));
            _sensorManager = sensorManager ?? throw new ArgumentNullException(nameof(sensorManager));
            _ioPanelOptions = ioPanelOptions?.Value ?? throw new ArgumentNullException(nameof(ioPanelOptions));
            _sensorOptions = sensorOptions?.Value ?? throw new ArgumentNullException(nameof(sensorOptions));
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

            // 步骤2：聚合 IoPanel 与 Sensor 点位并一次性下发监控注册。
            var monitoredPointIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var button in _ioPanelOptions.Buttons) {
                if (!string.IsNullOrWhiteSpace(button.PointId)) {
                    _ = monitoredPointIds.Add(button.PointId);
                }
            }

            foreach (var sensor in _sensorOptions.Sensors) {
                if (!string.IsNullOrWhiteSpace(sensor.PointId)) {
                    _ = monitoredPointIds.Add(sensor.PointId);
                }
            }

            var monitoredSet = await _emc.SetMonitoredIoPointsAsync(monitoredPointIds.ToArray(), stoppingToken).ConfigureAwait(false);
            if (!monitoredSet) {
                _logger.LogError("IoMonitoringHostedService 启动失败：EMC 点位下发未成功。");
                return;
            }

            // 步骤3：启动 IoPanel 与 Sensor 监控模块。
            await _ioPanelManager.StartMonitoringAsync(stoppingToken).ConfigureAwait(false);
            await _sensorManager.StartMonitoringAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("IoMonitoringHostedService 已启动，监控点数量={PointCount}。", monitoredPointIds.Count);

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
    }
}
