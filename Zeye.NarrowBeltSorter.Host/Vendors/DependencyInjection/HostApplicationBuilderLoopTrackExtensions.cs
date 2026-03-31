using Zeye.NarrowBeltSorter.Execution.Services;

namespace Zeye.NarrowBeltSorter.Host.Vendors.DependencyInjection {

    /// <summary>
    /// 环轨（LoopTrack）托管服务注册扩展。
    /// </summary>
    public static class HostApplicationBuilderLoopTrackExtensions {

        /// <summary>
        /// 按配置注册环轨托管服务：Hil.Enabled=true 时注册上机联调服务，否则按 Enabled 注册正式服务。
        /// </summary>
        /// <param name="builder">Host 构建器。</param>
        /// <returns>Host 构建器。</returns>
        public static HostApplicationBuilder AddLoopTrack(this HostApplicationBuilder builder) {
            var loopTrackEnabled = builder.Configuration.GetValue<bool>("LoopTrack:Enabled");
            var hilEnabled = builder.Configuration.GetValue<bool>("LoopTrack:Hil:Enabled");
            if (hilEnabled) {
                builder.Services.AddHostedService<LoopTrackHILHostedService>();
            }
            else if (loopTrackEnabled) {
                builder.Services.AddHostedService<LoopTrackManagerHostedService>();
            }
            return builder;
        }
    }
}
