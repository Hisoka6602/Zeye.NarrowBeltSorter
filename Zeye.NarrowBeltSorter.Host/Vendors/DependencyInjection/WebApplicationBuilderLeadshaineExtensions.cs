using Zeye.NarrowBeltSorter.Core.Options.Leadshaine;
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
            var leadshaineSection = builder.Configuration.GetSection("Leadshaine");
            var pointBindingsSection = leadshaineSection.GetSection("PointBindings");
            var ioPanelSection = leadshaineSection.GetSection("IoPanel");
            var sensorSection = leadshaineSection.GetSection("Sensor");

            var pointValidator = new LeadshainePointBindingOptionsValidator();
            var ioPanelValidator = new LeadshaineIoPanelButtonOptionsBindingValidator();
            var sensorValidator = new LeadshaineSensorOptionsBindingValidator();

            builder.Services.AddSingleton(pointValidator);
            builder.Services.AddSingleton(ioPanelValidator);
            builder.Services.AddSingleton(sensorValidator);

            builder.Services
                .AddOptions<LeadshaineEmcConnectionOptions>()
                .Bind(leadshaineSection.GetSection("EmcConnection"))
                .Validate(options => options.Validate().Count == 0, "Leadshaine.EmcConnection 配置不合法。")
                .ValidateOnStart();

            builder.Services
                .AddOptions<LeadshainePointBindingCollectionOptions>()
                .Bind(pointBindingsSection)
                .Validate(
                    options => pointValidator.Validate(options).Count == 0,
                    "Leadshaine.PointBindings 配置不合法。")
                .ValidateOnStart();

            var pointBindingsSnapshot = new LeadshainePointBindingCollectionOptions();
            pointBindingsSection.Bind(pointBindingsSnapshot);

            builder.Services
                .AddOptions<LeadshaineIoPanelButtonBindingCollectionOptions>()
                .Bind(ioPanelSection)
                .Validate(
                    options => ioPanelValidator.Validate(options, pointBindingsSnapshot).Count == 0,
                    "Leadshaine.IoPanel 配置不合法。")
                .ValidateOnStart();

            builder.Services
                .AddOptions<LeadshaineSensorBindingCollectionOptions>()
                .Bind(sensorSection)
                .Validate(
                    options => sensorValidator.Validate(options, pointBindingsSnapshot).Count == 0,
                    "Leadshaine.Sensor 配置不合法。")
                .ValidateOnStart();

            builder.Services
                .AddOptions<CorePointBindingOptions>()
                .Bind(pointBindingsSection)
                .ValidateOnStart();

            return builder;
        }
    }
}
