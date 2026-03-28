using NLog;
using NLog.Extensions.Logging;
using Zeye.NarrowBeltSorter.Host.Services;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Options.LogCleanup;
using Zeye.NarrowBeltSorter.Drivers.Vendors.ZhiQian;
using Zeye.NarrowBeltSorter.Core.Manager.Protocols;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Infrared;

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

var chutesEnabled = builder.Configuration.GetValue<bool>("Chutes:Enabled");
var chuteVendor = builder.Configuration.GetValue<string>("Chutes:Vendor") ?? string.Empty;
var zhiQianEnabled = builder.Configuration.GetValue<bool>("Chutes:ZhiQian:Enabled");
if (chutesEnabled && chuteVendor.Equals("ZhiQian", StringComparison.OrdinalIgnoreCase) && zhiQianEnabled) {
    RegisterZhiQianChuteManager(builder);
    var forcedRotationEnabled = builder.Configuration.GetValue<bool>("Chutes:ForcedRotation:Enabled");
    if (forcedRotationEnabled) {
        builder.Services.AddHostedService<ChuteForcedRotationService>();
    }
}

builder.Services.AddHostedService<LogCleanupService>();
var loopTrackEnabled = builder.Configuration.GetValue<bool>("LoopTrack:Enabled");
var hilEnabled = builder.Configuration.GetValue<bool>("LoopTrack:Hil:Enabled");
if (hilEnabled) {
    builder.Services.AddHostedService<LoopTrackHILWorker>();
}
else if (loopTrackEnabled) {
    builder.Services.AddHostedService<LoopTrackManagerService>();
}

var host = builder.Build();
var startupLog = LogManager.GetCurrentClassLogger();
startupLog.Info("Configuration startup mode. Environment={0}, UseEnvironmentOnlyConfig={1}", builder.Environment.EnvironmentName, ShouldUseEnvironmentOnlyConfig());
host.Run();

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
    builder.Services.AddSingleton<IInfraredDriverFrameCodec, LeadshaineInfraredDriverFrameCodec>();

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

static void ConfigureConfigurationSources(HostApplicationBuilder builder, string[] args) {
    var useEnvironmentOnlyConfig = ShouldUseEnvironmentOnlyConfig();
    builder.Configuration.Sources.Clear();

    if (!useEnvironmentOnlyConfig) {
        builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        builder.Configuration.AddJsonFile("appsettings.devices.json", optional: true, reloadOnChange: true);
    }

    builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
    builder.Configuration.AddJsonFile($"appsettings.devices.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
    builder.Configuration.AddEnvironmentVariables();
    builder.Configuration.AddCommandLine(args);
}

static bool ShouldUseEnvironmentOnlyConfig() {
    var setting = Environment.GetEnvironmentVariable("ZEYE_USE_ENV_ONLY_CONFIG");
    return bool.TryParse(setting, out var enabled) && enabled;
}
