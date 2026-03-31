using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Execution.Services;

namespace Zeye.NarrowBeltSorter.Host.Vendors.DependencyInjection {

    /// <summary>
    /// 分拣任务编排注册扩展。
    /// </summary>
    public static class HostApplicationBuilderSortingExtensions {

        /// <summary>
        /// 按配置注册分拣任务编排托管服务（EmcConnection.Enabled 且 ZhiQian 格口已启用时生效）。
        /// </summary>
        /// <param name="builder">Host 构建器。</param>
        /// <returns>Host 构建器。</returns>
        public static HostApplicationBuilder AddSortingTaskOrchestration(this HostApplicationBuilder builder) {
            // 步骤1：检查 EMC 连接是否启用。
            var emcOptions = builder.Configuration.GetSection("Leadshaine:EmcConnection")
                .Get<LeadshaineEmcConnectionOptions>();
            // 步骤2：检查格口总开关、厂商与智嵌开关是否启用。
            var chutesEnabled = builder.Configuration.GetValue<bool>("Chutes:Enabled");
            var chuteVendor = builder.Configuration.GetValue<string>("Chutes:Vendor") ?? string.Empty;
            var zhiQianEnabled = builder.Configuration.GetValue<bool>("Chutes:ZhiQian:Enabled");
            if (emcOptions?.Enabled != true
                || !chutesEnabled
                || !zhiQianEnabled
                || !chuteVendor.Equals("ZhiQian", StringComparison.OrdinalIgnoreCase)) {
                return builder;
            }

            // 步骤3：注册分拣编排依赖单例与托管服务。
            builder.Services.AddSingleton<SortingTaskCarrierLoadingService>();
            builder.Services.AddSingleton<SortingTaskDropOrchestrationService>();
            builder.Services.AddHostedService<SortingTaskOrchestrationService>();
            return builder;
        }
    }
}
