using Zeye.NarrowBeltSorter.Core.Options.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Manager.Emc;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Validators;
using CorePointBindingOptions = Zeye.NarrowBeltSorter.Core.Options.Leadshaine.LeadshainePointBindingOptions;

namespace Zeye.NarrowBeltSorter.Host.Vendors.DependencyInjection {
    /// <summary>
    /// Leadshaine 厂商配置注册扩展。
    /// </summary>
    public static class WebApplicationBuilderLeadshaineExtensions {
        /// <summary>
        /// 注册 Leadshaine EMC 配置与启动前校验。
        /// </summary>
        /// <param name="builder">Host 构建器。</param>
        /// <returns>Host 构建器。</returns>
        public static HostApplicationBuilder UseLeadshaineEmcVendor(this HostApplicationBuilder builder) {
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
                .Validate(options => options.Validate().Count == 0, "Leadshaine.EmcConnection 配置不合法。")
                .ValidateOnStart();

            // 步骤4：注册点位集合配置与地址合法性校验。
            builder.Services
                .AddOptions<LeadshainePointBindingCollectionOptions>()
                .Bind(pointBindingsSection)
                .Validate(
                    options => pointValidator.Validate(options).Count == 0,
                    "Leadshaine.PointBindings 配置不合法。")
                .ValidateOnStart();
            // 步骤4补充：当前 IoPanel/Sensor 引用校验使用启动阶段快照，后续热更新场景在 PR-3 统一收敛。
            var pointBindingsSnapshot = pointBindingsSection.Get<LeadshainePointBindingCollectionOptions>()
                ?? new LeadshainePointBindingCollectionOptions();

            // 步骤5：注册 IoPanel 按钮绑定。
            builder.Services
                .AddOptions<LeadshaineIoPanelButtonBindingCollectionOptions>()
                .Bind(ioPanelSection)
                .Validate(
                    options => ioPanelValidator.Validate(options, pointBindingsSnapshot).Count == 0,
                    "Leadshaine.IoPanel 配置不合法。")
                .ValidateOnStart();

            // 步骤6：注册 Sensor 绑定。
            builder.Services
                .AddOptions<LeadshaineSensorBindingCollectionOptions>()
                .Bind(sensorSection)
                .Validate(
                    options => sensorValidator.Validate(options, pointBindingsSnapshot).Count == 0,
                    "Leadshaine.Sensor 配置不合法。")
                .ValidateOnStart();

            // 步骤7：同步注册 Core 层点位配置对象，供后续 PR-2 控制器实现复用。
            builder.Services
                .AddOptions<CorePointBindingOptions>()
                .Bind(pointBindingsSection)
                .ValidateOnStart();

            // 步骤8：注册 EMC 控制器与硬件适配器，为 PR-2 控制器能力提供注入入口。
            builder.Services.AddSingleton<IEmcHardwareAdapter, LeadshaineEmcHardwareAdapter>();
            builder.Services.AddSingleton<IEmcController>(sp => new LeadshaineEmcController(
                sp.GetRequiredService<SafeExecutor>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshaineEmcConnectionOptions>>().Value,
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeadshainePointBindingCollectionOptions>>().Value,
                sp.GetRequiredService<IEmcHardwareAdapter>()));

            return builder;
        }
    }
}
