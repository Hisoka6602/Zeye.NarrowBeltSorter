using NLog;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Protocols;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;
using Zeye.NarrowBeltSorter.Drivers.Vendors.ZhiQian;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Infrared;
using Zeye.NarrowBeltSorter.Execution.Services;

namespace Zeye.NarrowBeltSorter.Host.Vendors.DependencyInjection {

    /// <summary>
    /// 智嵌（ZhiQian）格口厂商配置注册扩展。
    /// </summary>
    public static class HostApplicationBuilderZhiQianExtensions {

        /// <summary>
        /// 按配置注册智嵌格口管理器及相关依赖（Chutes.Enabled 且 Vendor=ZhiQian 且 ZhiQian.Enabled=true 时生效）。
        /// 同时按子开关注册强排与落格模拟托管服务。
        /// </summary>
        /// <param name="builder">Host 构建器。</param>
        /// <returns>Host 构建器。</returns>
        public static HostApplicationBuilder AddZhiQianChutes(this HostApplicationBuilder builder) {
            // 步骤1：检查格口总开关与厂商开关。
            var (chutesEnabled, chuteVendor, zhiQianEnabled) = GetZhiQianChuteConfig(builder);
            if (!chutesEnabled || !chuteVendor.Equals("ZhiQian", StringComparison.OrdinalIgnoreCase) || !zhiQianEnabled) {
                return builder;
            }

            // 步骤2：解析并校验智嵌格口配置，校验失败时仅记录错误并跳过注册。
            var log = LogManager.GetCurrentClassLogger();
            var options = builder.Configuration.GetSection("Chutes:ZhiQian").Get<ZhiQianChuteOptions>() ?? new ZhiQianChuteOptions();
            options.NormalizeLegacySingleDevice();
            var errors = options.Validate();
            if (errors.Count > 0) {
                foreach (var err in errors) {
                    log.Error("ZhiQian配置非法 error={0}", err);
                }
                return builder;
            }

            // 步骤3：注册格口驱动核心依赖与格口管理器单例。
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton<IZhiQianClientAdapterFactory, ZhiQianClientAdapterFactory>();
            builder.Services.AddSingleton<IInfraredDriverFrameCodec>(
                sp => new LeadshaineInfraredDriverFrameCodec(sp.GetRequiredService<SafeExecutor>()));
            builder.Services.AddSingleton<IChuteManager>(sp => {
                var factory = sp.GetRequiredService<IZhiQianClientAdapterFactory>();
                var device = options.Devices[0];
                var adapter = factory.Create(device, options);
                return new ZhiQianChuteManager(
                    options,
                    device,
                    adapter,
                    sp.GetRequiredService<SafeExecutor>(),
                    sp.GetRequiredService<IInfraredDriverFrameCodec>());
            });

            // 步骤4：注册格口自处理托管服务。
            builder.Services.AddHostedService<ChuteSelfHandlingHostedService>();

            // 步骤5：按配置注册强排托管服务。
            if (builder.Configuration.GetValue<bool>("Chutes:ForcedRotation:Enabled")) {
                builder.Services.AddHostedService<ChuteForcedRotationHostedService>();
            }

            // 步骤6：按配置注册落格模拟托管服务。
            if (builder.Configuration.GetValue<bool>("Chutes:DropSimulation:Enabled")) {
                builder.Services.AddHostedService<ChuteDropSimulationHostedService>();
            }

            return builder;
        }

        /// <summary>
        /// 读取智嵌格口相关配置开关（格口总开关、厂商名称、智嵌子开关）。
        /// </summary>
        /// <param name="builder">Host 构建器。</param>
        /// <returns>格口总开关、厂商名称与智嵌子开关元组。</returns>
        internal static (bool chutesEnabled, string chuteVendor, bool zhiQianEnabled) GetZhiQianChuteConfig(HostApplicationBuilder builder) {
            var chutesEnabled = builder.Configuration.GetValue<bool>("Chutes:Enabled");
            var chuteVendor = builder.Configuration.GetValue<string>("Chutes:Vendor") ?? string.Empty;
            var zhiQianEnabled = builder.Configuration.GetValue<bool>("Chutes:ZhiQian:Enabled");
            return (chutesEnabled, chuteVendor, zhiQianEnabled);
        }
    }
}
