using NLog;
using NLog.Extensions.Logging;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Execution.Parcel;
using Zeye.NarrowBeltSorter.Execution.Services;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;
using Zeye.NarrowBeltSorter.Core.Options.Carrier;
using Zeye.NarrowBeltSorter.Core.Manager.Protocols;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Options.LogCleanup;
using Zeye.NarrowBeltSorter.Drivers.Vendors.ZhiQian;
using Zeye.NarrowBeltSorter.Execution.Services.State;
using Zeye.NarrowBeltSorter.Execution.Services.Hosted;
using Zeye.NarrowBeltSorter.Execution.Services.Carrier;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Host.Vendors.DependencyInjection;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Infrared;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc.Options;
using Zeye.NarrowBeltSorter.Core.Options.Chutes.Zeye.NarrowBeltSorter.Core.Options.Chutes;

var builder = Host.CreateApplicationBuilder(args);
ConfigureConfigurationSources(builder, args);
var nlogConfigPath = Path.Combine(AppContext.BaseDirectory, "NLog.config");
LogManager.Setup().LoadConfigurationFromFile(nlogConfigPath);
builder.Logging.ClearProviders();
builder.Logging.AddNLog(new NLogProviderOptions {
    RemoveLoggerFactoryFilter = false,
    CaptureEventId = EventIdCaptureType.EventId,
    CaptureMessageParameters = true,
});
builder.Services.AddSingleton<SafeExecutor>();
builder.Services.Configure<LogCleanupSettings>(builder.Configuration.GetSection("LogCleanup"));
builder.Services.Configure<LoopTrackServiceOptions>(builder.Configuration.GetSection("LoopTrack"));
builder.Services.Configure<ChuteForcedRotationOptions>(builder.Configuration.GetSection("Chutes:ForcedRotation"));
builder.Services.Configure<ChuteDropSimulationOptions>(builder.Configuration.GetSection("Chutes:DropSimulation"));
builder.Services.Configure<LeadshaineIoLinkageOptions>(builder.Configuration.GetSection("Leadshaine:IoLinkage"));
builder.Services.Configure<CarrierManagerOptions>(builder.Configuration.GetSection("Carrier"));
builder.AddLeadshaineEmcVendor();
RegisterLeadshaineIoMonitoring(builder);
builder.Services.AddSingleton<ISystemStateManager, LocalSystemStateManager>();
builder.Services.AddSingleton<ICarrierManager, InfraredSensorCarrierManager>();
builder.Services.AddSingleton<IParcelManager, ParcelManager>();
RegisterIoPanelStateTransition(builder);
RegisterLeadshaineIoLinkage(builder);
RegisterCarrierLoopGrouping(builder);
RegisterSortingTaskOrchestration(builder);

var chutesEnabled = builder.Configuration.GetValue<bool>("Chutes:Enabled");
var chuteVendor = builder.Configuration.GetValue<string>("Chutes:Vendor") ?? string.Empty;
var zhiQianEnabled = builder.Configuration.GetValue<bool>("Chutes:ZhiQian:Enabled");
if (chutesEnabled && chuteVendor.Equals("ZhiQian", StringComparison.OrdinalIgnoreCase) && zhiQianEnabled) {
    RegisterZhiQianChuteManager(builder);
    builder.Services.AddHostedService<ChuteSelfHandlingHostedService>();
    var forcedRotationEnabled = builder.Configuration.GetValue<bool>("Chutes:ForcedRotation:Enabled");
    if (forcedRotationEnabled) {
        builder.Services.AddHostedService<ChuteForcedRotationHostedService>();
    }
    var dropSimulationEnabled = builder.Configuration.GetValue<bool>("Chutes:DropSimulation:Enabled");
    if (dropSimulationEnabled) {
        builder.Services.AddHostedService<ChuteDropSimulationHostedService>();
    }
}

builder.Services.AddHostedService<LogCleanupHostedService>();
var loopTrackEnabled = builder.Configuration.GetValue<bool>("LoopTrack:Enabled");
var hilEnabled = builder.Configuration.GetValue<bool>("LoopTrack:Hil:Enabled");
if (hilEnabled) {
    builder.Services.AddHostedService<LoopTrackHILHostedService>();
}
else if (loopTrackEnabled) {
    builder.Services.AddHostedService<LoopTrackManagerHostedService>();
}

var host = builder.Build();
var startupLog = LogManager.GetCurrentClassLogger();
startupLog.Info("Configuration startup mode. Environment={0}, UseEnvironmentOnlyConfig={1}", builder.Environment.EnvironmentName, ShouldUseEnvironmentOnlyConfig());
host.Run();

/// <summary>
/// 注册智嵌格口管理器及相关依赖。
/// </summary>
/// <param name="builder">Host 构建器。</param>
static void RegisterZhiQianChuteManager(HostApplicationBuilder builder) {
    var log = LogManager.GetCurrentClassLogger();
    var options = builder.Configuration.GetSection("Chutes:ZhiQian").Get<ZhiQianChuteOptions>() ?? new ZhiQianChuteOptions();
    options.NormalizeLegacySingleDevice();
    var errors = options.Validate();
    if (errors.Count > 0) {
        foreach (var err in errors) {
            log.Error("ZhiQian配置非法 error={0}", err);
        }

        return;
    }

    builder.Services.AddSingleton(options);
    builder.Services.AddSingleton<IZhiQianClientAdapterFactory, ZhiQianClientAdapterFactory>();
    builder.Services.AddSingleton<IInfraredDriverFrameCodec>(sp => new LeadshaineInfraredDriverFrameCodec(sp.GetRequiredService<SafeExecutor>()));

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
}

/// <summary>
/// 配置应用配置源加载顺序。
/// </summary>
/// <param name="builder">Host 构建器。</param>
/// <param name="args">启动参数。</param>
static void ConfigureConfigurationSources(HostApplicationBuilder builder, string[] args) {
    var useEnvironmentOnlyConfig = ShouldUseEnvironmentOnlyConfig();
    builder.Configuration.Sources.Clear();

    if (!useEnvironmentOnlyConfig) {
        builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        builder.Configuration.AddJsonFile("appsettings.devices.json", optional: true, reloadOnChange: true);
    }

    builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
    builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.devices.json", optional: true, reloadOnChange: true);
    builder.Configuration.AddEnvironmentVariables();
    builder.Configuration.AddCommandLine(args);
}

/// <summary>
/// 判断是否仅使用环境变量配置启动。
/// </summary>
/// <returns>是否启用仅环境变量配置模式。</returns>
static bool ShouldUseEnvironmentOnlyConfig() {
    var setting = Environment.GetEnvironmentVariable("ZEYE_USE_ENV_ONLY_CONFIG");
    return bool.TryParse(setting, out var enabled) && enabled;
}

/// <summary>
/// 按配置注册 Leadshaine Io 监控托管服务。
/// </summary>
/// <param name="builder">Host 构建器。</param>
static void RegisterLeadshaineIoMonitoring(HostApplicationBuilder builder) {
    var options = builder.Configuration.GetSection("Leadshaine:EmcConnection").Get<LeadshaineEmcConnectionOptions>();
    if (options?.Enabled != true) {
        return;
    }

    builder.Services.AddHostedService<IoMonitoringHostedService>();
}

/// <summary>
/// 按配置注册 Leadshaine 联动 Io 托管服务。
/// </summary>
/// <param name="builder">Host 构建器。</param>
static void RegisterLeadshaineIoLinkage(HostApplicationBuilder builder) {
    var emcOptions = builder.Configuration.GetSection("Leadshaine:EmcConnection").Get<LeadshaineEmcConnectionOptions>();
    var linkageOptions = builder.Configuration.GetSection("Leadshaine:IoLinkage").Get<LeadshaineIoLinkageOptions>();
    if (emcOptions?.Enabled != true || linkageOptions?.Enabled != true) {
        return;
    }

    builder.Services.AddHostedService<IoLinkageHostedService>();
}

/// <summary>
/// 按配置注册 IoPanel 到系统状态桥接托管服务。
/// </summary>
/// <param name="builder">Host 构建器。</param>
static void RegisterIoPanelStateTransition(HostApplicationBuilder builder) {
    var options = builder.Configuration.GetSection("Leadshaine:EmcConnection").Get<LeadshaineEmcConnectionOptions>();
    if (options?.Enabled != true) {
        return;
    }

    builder.Services.AddHostedService<IoPanelStateTransitionHostedService>();
}
/// <summary>
/// 按配置注册小车环组统计托管服务。
/// </summary>
/// <param name="builder">Host 构建器。</param>
static void RegisterCarrierLoopGrouping(HostApplicationBuilder builder) {
    var emcOptions = builder.Configuration.GetSection("Leadshaine:EmcConnection").Get<LeadshaineEmcConnectionOptions>();
    var sensorOptions = builder.Configuration.GetSection("Leadshaine:Sensor").Get<LeadshaineSensorBindingCollectionOptions>();
    if (emcOptions?.Enabled != true || sensorOptions?.Sensors is null || sensorOptions.Sensors.Count == 0) {
        return;
    }

    builder.Services.AddHostedService<CarrierLoopGroupingHostedService>();
}
/// <summary>
/// 按配置注册分拣任务编排托管服务。
/// </summary>
/// <param name="builder">Host 构建器。</param>
static void RegisterSortingTaskOrchestration(HostApplicationBuilder builder) {
    var emcOptions = builder.Configuration.GetSection("Leadshaine:EmcConnection").Get<LeadshaineEmcConnectionOptions>();
    var chutesEnabled = builder.Configuration.GetValue<bool>("Chutes:Enabled");
    var chuteVendor = builder.Configuration.GetValue<string>("Chutes:Vendor") ?? string.Empty;
    var zhiQianEnabled = builder.Configuration.GetValue<bool>("Chutes:ZhiQian:Enabled");
    if (emcOptions?.Enabled != true ||
        !chutesEnabled ||
        !zhiQianEnabled ||
        !chuteVendor.Equals("ZhiQian", StringComparison.OrdinalIgnoreCase)) {
        return;
    }

    builder.Services.AddHostedService<SortingTaskOrchestrationService>();
}
