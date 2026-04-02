using Microsoft.Extensions.Configuration;

namespace Zeye.NarrowBeltSorter.Host.Vendors.DependencyInjection {

    /// <summary>
    /// Host 应用配置源扩展，负责按环境加载多层配置文件。
    /// </summary>
    public static class HostApplicationBuilderConfigurationExtensions {

        /// <summary>
        /// 配置应用配置源加载顺序：基础配置 → 按能力拆分的可选配置 → 环境变量 → 命令行。
        /// 若 ZEYE_USE_ENV_ONLY_CONFIG=true，则跳过所有 JSON 文件，仅使用环境变量与命令行参数。
        /// </summary>
        /// <param name="builder">Host 构建器。</param>
        /// <param name="args">命令行启动参数。</param>
        public static HostApplicationBuilder ConfigureConfigurationSources(
            this HostApplicationBuilder builder,
            string[] args) {
            var useEnvironmentOnlyConfig = IsEnvironmentOnlyConfig();
            builder.Configuration.Sources.Clear();

            if (!useEnvironmentOnlyConfig) {
                // 步骤1：加载全局基础配置（通用开关、日志清理、Carrier 等）。
                builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                // 步骤2：加载环轨拆分配置（LoopTrack 能力模块默认值）。
                builder.Configuration.AddJsonFile("appsettings.looptrack.json", optional: true, reloadOnChange: true);
                // 步骤3：加载格口拆分配置（Chutes + Carrier 能力模块默认值）。
                builder.Configuration.AddJsonFile("appsettings.chutes.json", optional: true, reloadOnChange: true);
                // 步骤4：加载 Leadshaine EMC 拆分配置（EMC/IoPanel/Sensor/SignalTower/IoLinkage 默认值）。
                builder.Configuration.AddJsonFile("appsettings.leadshaine.json", optional: true, reloadOnChange: true);
                // 步骤5：加载设备硬件参数分片（覆盖能力默认值，按职责拆分）。
                builder.Configuration.AddJsonFile("appsettings.devices.looptrack.json", optional: true, reloadOnChange: true);
                builder.Configuration.AddJsonFile("appsettings.devices.chutes.json", optional: true, reloadOnChange: true);
            }

            // 步骤6：环境变量与命令行参数（最高优先级，可覆盖所有文件配置）。
            builder.Configuration.AddEnvironmentVariables();
            builder.Configuration.AddCommandLine(args);
            return builder;
        }

        /// <summary>
        /// 判断是否仅使用环境变量配置启动（ZEYE_USE_ENV_ONLY_CONFIG=true 时生效）。
        /// </summary>
        private static bool IsEnvironmentOnlyConfig() {
            var setting = Environment.GetEnvironmentVariable("ZEYE_USE_ENV_ONLY_CONFIG");
            return bool.TryParse(setting, out var enabled) && enabled;
        }
    }
}
