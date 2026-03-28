using NLog;
using System.IO.Ports;
using NLog.Extensions.Logging;
using Zeye.NarrowBeltSorter.Host.Services;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.Chutes;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Options.LogCleanup;
using Zeye.NarrowBeltSorter.Drivers.Vendors.ZhiQian;

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

// ZhiQian 格口管理器注册（同时满足：总开关 Enabled、Vendor=="ZhiQian"、子驱动 Enabled）
var chutesEnabled = builder.Configuration.GetValue<bool>("Chutes:Enabled");
var chuteVendor = builder.Configuration.GetValue<string>("Chutes:Vendor") ?? string.Empty;
var zhiQianEnabled = builder.Configuration.GetValue<bool>("Chutes:ZhiQian:Enabled");
if (chutesEnabled
    && chuteVendor.Equals("ZhiQian", StringComparison.OrdinalIgnoreCase)
    && zhiQianEnabled) {
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
    var options = builder.Configuration
        .GetSection("Chutes:ZhiQian")
        .Get<ZhiQianChuteOptions>() ?? new ZhiQianChuteOptions();
    var errors = options.Validate();
    if (errors.Count > 0) {
        foreach (var err in errors) {
            log.Error("ZhiQian配置非法 error={0}", err);
        }

        return;
    }

    var adapter = BuildZhiQianAdapter(options);
    builder.Services.AddSingleton<IZhiQianModbusClientAdapter>(_ => adapter);
    builder.Services.AddSingleton<IChuteManager>(sp => new ZhiQianChuteManager(options, adapter, sp.GetRequiredService<SafeExecutor>()));
}

static IZhiQianModbusClientAdapter BuildZhiQianAdapter(ZhiQianChuteOptions options) {
    if (options.Transport == ZhiQianTransport.ModbusTcp) {
        return new ZhiQianModbusClientAdapter(
            options.Host,
            options.Port,
            options.DeviceAddress,
            options.CommandTimeoutMs,
            options.RetryCount,
            options.RetryDelayMs);
    }

    return new ZhiQianModbusClientAdapter(
        options.SerialPortName,
        options.BaudRate,
        Enum.Parse<Parity>(options.Parity, ignoreCase: true),
        options.DataBits,
        Enum.Parse<StopBits>(options.StopBits, ignoreCase: true),
        options.DeviceAddress,
        options.CommandTimeoutMs,
        options.RetryCount,
        options.RetryDelayMs);
}
static void ConfigureConfigurationSources(HostApplicationBuilder builder, string[] args) {
    var useEnvironmentOnlyConfig = ShouldUseEnvironmentOnlyConfig();
    builder.Configuration.Sources.Clear();

    if (!useEnvironmentOnlyConfig) {
        builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    }

    builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
    builder.Configuration.AddEnvironmentVariables();
    builder.Configuration.AddCommandLine(args);
}

static bool ShouldUseEnvironmentOnlyConfig() {
    var setting = Environment.GetEnvironmentVariable("ZEYE_USE_ENV_ONLY_CONFIG");
    return bool.TryParse(setting, out var enabled) && enabled;
}
