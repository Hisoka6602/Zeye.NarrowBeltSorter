using NLog;
using NLog.Extensions.Logging;
using Zeye.NarrowBeltSorter.Host.Services;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Options.LogCleanup;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Enums.Chutes;
using Zeye.NarrowBeltSorter.Drivers.Vendors.ZhiQian;
using System.IO.Ports;

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

// ZhiQian 格口管理器注册（按 Chutes:Enabled 决定是否启用）
var chutesEnabled = builder.Configuration.GetValue<bool>("Chutes:Enabled");
if (chutesEnabled) {
    RegisterZhiQianChuteManager(builder);
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
/// 读取 Chutes:ZhiQian 配置并注册智嵌格口管理器（IChuteManager / IZhiQianModbusClientAdapter）。
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

    var adapter = BuildZhiQianAdapter(options);
    builder.Services.AddSingleton<IZhiQianModbusClientAdapter>(_ => adapter);
    builder.Services.AddSingleton<IChuteManager>(sp =>
        new ZhiQianChuteManager(options, adapter, sp.GetRequiredService<SafeExecutor>()));
}

/// <summary>
/// 按 Transport 模式构建智嵌 Modbus 客户端适配器（ModbusTcp / ModbusRtu）。
/// </summary>
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
