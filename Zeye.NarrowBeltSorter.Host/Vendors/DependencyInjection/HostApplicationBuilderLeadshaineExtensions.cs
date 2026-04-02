using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Manager.Emc;
using Zeye.NarrowBeltSorter.Execution.Services;
using Zeye.NarrowBeltSorter.Core.Manager.Sensor;
using Zeye.NarrowBeltSorter.Core.Manager.IoPanel;
using Zeye.NarrowBeltSorter.Core.Manager.SignalTower;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Sensor;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Validators;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.SignalTower;
using Zeye.NarrowBeltSorter.Execution.Services.Hosted;

namespace Zeye.NarrowBeltSorter.Host.Vendors.DependencyInjection {

    /// <summary>
    /// Leadshaine 厂商配置注册扩展。
    /// </summary>
    public static class HostApplicationBuilderLeadshaineExtensions {

        /// <summary>
        /// 注册 Leadshaine EMC 配置与启动前校验。
        /// </summary>
        /// <param name="builder">Host 构建器。</param>
        /// <returns>Host 构建器。</returns>
        public static HostApplicationBuilder AddLeadshaineEmcVendor(this HostApplicationBuilder builder) {
            // 步骤1：解析 Leadshaine 各配置分段，后续统一按分段执行绑定与校验。
            var leadshaineSection = builder.Configuration.GetSection("Leadshaine");
            var pointBindingsSection = leadshaineSection.GetSection("PointBindings");
            var ioPanelSection = leadshaineSection.GetSection("IoPanel");
            var sensorSection = leadshaineSection.GetSection("Sensor");

            // 步骤2：初始化并注册校验器实例，确保启动时可以复用相同校验逻辑。
            var pointValidator = new LeadshainePointBindingOptionsValidator();
            var ioPanelValidator = new LeadshaineIoPanelButtonOptionsBindingValidator();
            var sensorValidator = new LeadshaineSensorOptionsBindingValidator();

            builder.Services.AddSingleton(pointValidator);
            builder.Services.AddSingleton(ioPanelValidator);
            builder.Services.AddSingleton(sensorValidator);

            // 步骤3：注册 EMC 连接配置与边界校验。
            builder.Services
                .AddOptions<LeadshaineEmcConnectionOptions>()
                .Bind(leadshaineSection.GetSection("EmcConnection"))
                .ValidateOnStart();
            builder.Services.AddSingleton<IValidateOptions<LeadshaineEmcConnectionOptions>>(
                _ => new LeadshaineOptionsDelegateValidator<LeadshaineEmcConnectionOptions>(
                    null,
                    static options => options.Validate()));

            // 步骤4：注册点位集合配置与地址合法性校验。
            builder.Services
                .AddOptions<LeadshaineIoPointBindingCollectionOptions>()
                .Bind(pointBindingsSection)
                .ValidateOnStart();
            builder.Services.AddSingleton<IValidateOptions<LeadshaineIoPointBindingCollectionOptions>>(
                _ => new LeadshaineOptionsDelegateValidator<LeadshaineIoPointBindingCollectionOptions>(
                    null,
                    pointValidator.Validate));
            // 步骤4补充：当前 IoPanel/Sensor 引用校验使用启动阶段快照，后续热更新场景在 PR-3 统一收敛。
            var pointBindingsSnapshot = pointBindingsSection.Get<LeadshaineIoPointBindingCollectionOptions>()
                ?? new LeadshaineIoPointBindingCollectionOptions();
            var snapshotErrors = pointValidator.Validate(pointBindingsSnapshot);
            if (snapshotErrors.Count > 0) {
                throw new InvalidOperationException($"Leadshaine.PointBindings 快照校验失败：{string.Join(" | ", snapshotErrors)}");
            }

            // 步骤5：注册 IoPanel 按钮绑定。
            builder.Services
                .AddOptions<LeadshaineIoPanelButtonBindingCollectionOptions>()
                .Bind(ioPanelSection)
                .ValidateOnStart();
            builder.Services.AddSingleton<IValidateOptions<LeadshaineIoPanelButtonBindingCollectionOptions>>(
                _ => new LeadshaineOptionsDelegateValidator<LeadshaineIoPanelButtonBindingCollectionOptions>(
                    null,
                    options => ioPanelValidator.Validate(options, pointBindingsSnapshot)));
            // 步骤6：注册 SignalTower 绑定配置。
            builder.Services
                .AddOptions<LeadshaineSignalTowerOptions>()
                .Bind(leadshaineSection.GetSection("SignalTower"))
                .ValidateOnStart();
            // 步骤6补充：注册 Sensor 绑定。
            builder.Services
                .AddOptions<LeadshaineSensorBindingCollectionOptions>()
                .Bind(sensorSection)
                .ValidateOnStart();
            builder.Services.AddSingleton<IValidateOptions<LeadshaineSensorBindingCollectionOptions>>(
                _ => new LeadshaineOptionsDelegateValidator<LeadshaineSensorBindingCollectionOptions>(
                    null,
                    options => sensorValidator.Validate(options, pointBindingsSnapshot)));

            // 步骤7：注册 EMC 控制器与硬件适配器。
            builder.Services.AddSingleton<IEmcHardwareAdapter, LeadshaineEmcHardwareAdapter>();
            builder.Services.AddSingleton<IEmcController>(sp => new LeadshaineEmcController(
                sp.GetRequiredService<SafeExecutor>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshaineEmcConnectionOptions>>().Value,
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshaineIoPointBindingCollectionOptions>>().Value,
                sp.GetRequiredService<IEmcHardwareAdapter>()));

            // 步骤8：注册 IoPanel 与 Sensor 管理器，供托管服务编排使用。
            builder.Services.AddSingleton<LeadshaineIoPanel>(sp => new LeadshaineIoPanel(
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LeadshaineIoPanel>>(),
                sp.GetRequiredService<SafeExecutor>(),
                sp.GetRequiredService<IEmcController>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshaineIoPanelButtonBindingCollectionOptions>>().Value,
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshaineIoPointBindingCollectionOptions>>().Value,
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshaineEmcConnectionOptions>>().Value));
            builder.Services.AddSingleton<IIoPanel>(sp => sp.GetRequiredService<LeadshaineIoPanel>());
            builder.Services.AddSingleton<LeadshaineSensorManager>(sp => new LeadshaineSensorManager(
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LeadshaineSensorManager>>(),
                sp.GetRequiredService<SafeExecutor>(),
                sp.GetRequiredService<IEmcController>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshaineSensorBindingCollectionOptions>>().Value,
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshaineIoPointBindingCollectionOptions>>().Value,
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshaineEmcConnectionOptions>>().Value));
            builder.Services.AddSingleton<ISensorManager>(sp => sp.GetRequiredService<LeadshaineSensorManager>());
            // 步骤9：按配置启用 EMC 信号塔实现。
            var signalTowerOptions = leadshaineSection.GetSection("SignalTower").Get<LeadshaineSignalTowerOptions>() ?? new LeadshaineSignalTowerOptions();
            if (signalTowerOptions.Enabled) {
                builder.Services.AddSingleton<ISignalTower>(sp => new EmcSignalTower(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EmcSignalTower>>(),
                    sp.GetRequiredService<SafeExecutor>(),
                    sp.GetRequiredService<IEmcController>(),
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshaineSignalTowerOptions>>().Value,
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshaineIoPointBindingCollectionOptions>>().Value));
                builder.Services.AddHostedService<SignalTowerHostedService>();
            }
            return builder;
        }

        /// <summary>
        /// 按配置注册 Leadshaine IO 监控托管服务（EmcConnection.Enabled=true 时生效）。
        /// </summary>
        /// <param name="builder">Host 构建器。</param>
        /// <returns>Host 构建器。</returns>
        public static HostApplicationBuilder AddLeadshaineIoMonitoring(this HostApplicationBuilder builder) {
            var options = builder.Configuration.GetSection("Leadshaine:EmcConnection").Get<LeadshaineEmcConnectionOptions>();
            if (options?.Enabled != true) {
                return builder;
            }
            builder.Services.AddHostedService<IoMonitoringHostedService>();
            return builder;
        }

        /// <summary>
        /// 按配置注册 Leadshaine IoPanel 到系统状态桥接托管服务，并绑定启动预警时长选项。
        /// EmcConnection.Enabled=true 时生效。
        /// </summary>
        /// <param name="builder">Host 构建器。</param>
        /// <returns>Host 构建器。</returns>
        public static HostApplicationBuilder AddLeadshaineIoPanelStateTransition(this HostApplicationBuilder builder) {
            var options = builder.Configuration.GetSection("Leadshaine:EmcConnection").Get<LeadshaineEmcConnectionOptions>();
            if (options?.Enabled != true) {
                return builder;
            }
            // 绑定 IoPanel 状态桥接时序参数，供 SignalTowerHostedService 使用。
            builder.Services
                .AddOptions<LeadshaineIoPanelStateTransitionOptions>()
                .Bind(builder.Configuration.GetSection("Leadshaine:IoPanelStateTransition"));
            builder.Services.AddHostedService<IoPanelStateTransitionHostedService>();
            return builder;
        }

        /// <summary>
        /// 按配置注册 Leadshaine 联动 IO 托管服务（EmcConnection.Enabled 且 IoLinkage.Enabled=true 时生效）。
        /// </summary>
        /// <param name="builder">Host 构建器。</param>
        /// <returns>Host 构建器。</returns>
        public static HostApplicationBuilder AddLeadshaineIoLinkage(this HostApplicationBuilder builder) {
            var emcOptions = builder.Configuration.GetSection("Leadshaine:EmcConnection").Get<LeadshaineEmcConnectionOptions>();
            var linkageOptions = builder.Configuration.GetSection("Leadshaine:IoLinkage").Get<LeadshaineIoLinkageOptions>();
            if (emcOptions?.Enabled != true || linkageOptions?.Enabled != true) {
                return builder;
            }
            builder.Services.AddHostedService<IoLinkageHostedService>();
            return builder;
        }

        /// <summary>
        /// 按配置注册小车环组统计托管服务（EmcConnection.Enabled 且 Sensor 绑定非空时生效）。
        /// </summary>
        /// <param name="builder">Host 构建器。</param>
        /// <returns>Host 构建器。</returns>
        public static HostApplicationBuilder AddLeadshaineCarrierLoopGrouping(this HostApplicationBuilder builder) {
            var emcOptions = builder.Configuration.GetSection("Leadshaine:EmcConnection").Get<LeadshaineEmcConnectionOptions>();
            var sensorOptions = builder.Configuration.GetSection("Leadshaine:Sensor").Get<LeadshaineSensorBindingCollectionOptions>();
            if (emcOptions?.Enabled != true || sensorOptions?.Sensors is null || sensorOptions.Sensors.Count == 0) {
                return builder;
            }
            builder.Services.AddHostedService<CarrierLoopGroupingHostedService>();
            return builder;
        }
    }
}
