using NLog;
using NLog.Extensions.Logging;
using Zeye.NarrowBeltSorter.Host.Services;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Options.LogCleanup;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Drivers.Vendors.ZhiQian;

var builder = Host.CreateApplicationBuilder(args);
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
host.Run();

/// <summary>
/// 读取 Chutes:ZhiQian 配置并注册智嵌格口管理器。
/// 支持多台继电器设备（每台设备独立 TCP 连接通道，通道内指令严格串行）：
/// - 单台设备时注册 IChuteManager → ZhiQianChuteManager；
/// - 多台设备时注册 IChuteManager → ZhiQianCompositeChuteManager（聚合路由，强排全局唯一）。
/// 配置校验失败时记录日志并跳过注册，避免程序崩溃。
/// </summary>
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

    if (options.Devices.Count == 1) {
        // 单设备：通过工厂 lambda 延迟构造，让 DI 容器在首次解析时自动提供 SafeExecutor。
        var deviceOpts = options.Devices[0];
        var adapter = new ZhiQianBinaryClientAdapter(
            deviceOpts.Host,
            deviceOpts.Port,
            deviceOpts.DeviceAddress,
            options.CommandTimeoutMs,
            options.RetryCount,
            options.RetryDelayMs);
        builder.Services.AddSingleton<IZhiQianClientAdapter>(_ => adapter);
        builder.Services.AddSingleton<IChuteManager>(sp =>
            new ZhiQianChuteManager(options, deviceOpts, adapter, sp.GetRequiredService<SafeExecutor>()));
    }
    else {
        // 多设备：通过工厂 lambda 延迟构造，让 DI 容器在首次解析时自动提供 SafeExecutor。
        var deviceSnapshots = options.Devices
            .Select(d => (deviceOpts: d, adapter: new ZhiQianBinaryClientAdapter(
                d.Host, d.Port, d.DeviceAddress,
                options.CommandTimeoutMs, options.RetryCount, options.RetryDelayMs)))
            .ToList();
        builder.Services.AddSingleton<IChuteManager>(sp => {
            var safeExecutor = sp.GetRequiredService<SafeExecutor>();
            var managers = deviceSnapshots
                .Select(t => new ZhiQianChuteManager(options, t.deviceOpts, t.adapter, safeExecutor))
                .ToList();
            return new ZhiQianCompositeChuteManager(managers);
        });
    }
}
