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
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc.Options;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.SignalTower;
using Zeye.NarrowBeltSorter.Core.Options.SignalTower.Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using CorePointBindingOptions = Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine.LeadshaineIoPointBindingCollectionOptions;

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
                .AddOptions<LeadshainePointBindingCollectionOptions>()
                .Bind(pointBindingsSection)
                .ValidateOnStart();
            builder.Services.AddSingleton<IValidateOptions<LeadshainePointBindingCollectionOptions>>(
                _ => new LeadshaineOptionsDelegateValidator<LeadshainePointBindingCollectionOptions>(
                    null,
                    pointValidator.Validate));
            // 步骤4补充：当前 IoPanel/Sensor 引用校验使用启动阶段快照，后续热更新场景在 PR-3 统一收敛。
            var pointBindingsSnapshot = pointBindingsSection.Get<LeadshainePointBindingCollectionOptions>()
                ?? new LeadshainePointBindingCollectionOptions();
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
            // 步骤6.1：注册 SignalTower 绑定配置。
            builder.Services
                .AddOptions<LeadshaineSignalTowerOptions>()
                .Bind(leadshaineSection.GetSection("SignalTower"))
                .ValidateOnStart();
            // 步骤6：注册 Sensor 绑定。
            builder.Services
                .AddOptions<LeadshaineSensorBindingCollectionOptions>()
                .Bind(sensorSection)
                .ValidateOnStart();
            builder.Services.AddSingleton<IValidateOptions<LeadshaineSensorBindingCollectionOptions>>(
                _ => new LeadshaineOptionsDelegateValidator<LeadshaineSensorBindingCollectionOptions>(
                    null,
                    options => sensorValidator.Validate(options, pointBindingsSnapshot)));

            // 步骤7：同步注册 Core 层点位配置对象，供后续 PR-2 控制器实现复用。
            builder.Services
                .AddOptions<CorePointBindingOptions>()
                .Bind(pointBindingsSection)
                .ValidateOnStart();
            builder.Services.AddSingleton<CorePointBindingOptions>(sp =>
                sp.GetRequiredService<IOptions<CorePointBindingOptions>>().Value);
            // 步骤8：注册 EMC 控制器与硬件适配器，为 PR-2 控制器能力提供注入入口。
            builder.Services.AddSingleton<IEmcHardwareAdapter, LeadshaineEmcHardwareAdapter>();
            builder.Services.AddSingleton<IEmcController>(sp => new LeadshaineEmcController(
                sp.GetRequiredService<SafeExecutor>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshaineEmcConnectionOptions>>().Value,
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshainePointBindingCollectionOptions>>().Value,
                sp.GetRequiredService<IEmcHardwareAdapter>()));

            // 步骤9：注册 IoPanel 与 Sensor 管理器，供托管服务编排使用。
            builder.Services.AddSingleton<LeadshaineIoPanel>(sp => new LeadshaineIoPanel(
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LeadshaineIoPanel>>(),
                sp.GetRequiredService<SafeExecutor>(),
                sp.GetRequiredService<IEmcController>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshaineIoPanelButtonBindingCollectionOptions>>().Value,
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshainePointBindingCollectionOptions>>().Value,
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshaineEmcConnectionOptions>>().Value));
            builder.Services.AddSingleton<IIoPanel>(sp => sp.GetRequiredService<LeadshaineIoPanel>());
            builder.Services.AddSingleton<LeadshaineSensorManager>(sp => new LeadshaineSensorManager(
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LeadshaineSensorManager>>(),
                sp.GetRequiredService<SafeExecutor>(),
                sp.GetRequiredService<IEmcController>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshaineSensorBindingCollectionOptions>>().Value,
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshainePointBindingCollectionOptions>>().Value,
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshaineEmcConnectionOptions>>().Value));
            builder.Services.AddSingleton<ISensorManager>(sp => sp.GetRequiredService<LeadshaineSensorManager>());
            // 步骤10：按配置启用 EMC 信号塔实现。
            var signalTowerOptions = leadshaineSection.GetSection("SignalTower").Get<LeadshaineSignalTowerOptions>() ?? new LeadshaineSignalTowerOptions();
            if (signalTowerOptions.Enabled) {
                builder.Services.AddSingleton<ISignalTower>(sp => new EmcSignalTower(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EmcSignalTower>>(),
                    sp.GetRequiredService<SafeExecutor>(),
                    sp.GetRequiredService<IEmcController>(),
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshaineSignalTowerOptions>>().Value,
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshainePointBindingCollectionOptions>>().Value));
                builder.Services.AddHostedService<SignalTowerHostedService>();
            }
            return builder;
        }
    }
}
