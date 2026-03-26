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

// ZhiQian 格口管理器注册（按 Chutes:Enabled 与 Chutes:Vendor 决定是否启用）
var chutesEnabled = builder.Configuration.GetValue<bool>("Chutes:Enabled");
if (chutesEnabled) {
    var zhiQianOptions = builder.Configuration
        .GetSection("Chutes:ZhiQian")
        .Get<ZhiQianChuteOptions>() ?? new ZhiQianChuteOptions();
    var validationErrors = zhiQianOptions.Validate();
    if (validationErrors.Count > 0) {
        foreach (var err in validationErrors) {
            LogManager.GetCurrentClassLogger().Error("ZhiQian配置非法 error={0}", err);
        }
    }
    else {
        var adapter = zhiQianOptions.Transport == ZhiQianTransport.ModbusTcp
            ? new ZhiQianModbusClientAdapter(
                zhiQianOptions.Host,
                zhiQianOptions.Port,
                zhiQianOptions.DeviceAddress,
                zhiQianOptions.CommandTimeoutMs,
                zhiQianOptions.RetryCount,
                zhiQianOptions.RetryDelayMs)
            : (IZhiQianModbusClientAdapter)new ZhiQianModbusClientAdapter(
                zhiQianOptions.SerialPortName,
                zhiQianOptions.BaudRate,
                Enum.Parse<Parity>(zhiQianOptions.Parity, ignoreCase: true),
                zhiQianOptions.DataBits,
                Enum.Parse<StopBits>(zhiQianOptions.StopBits, ignoreCase: true),
                zhiQianOptions.DeviceAddress,
                zhiQianOptions.CommandTimeoutMs,
                zhiQianOptions.RetryCount,
                zhiQianOptions.RetryDelayMs);
        builder.Services.AddSingleton<IZhiQianModbusClientAdapter>(_ => adapter);
        builder.Services.AddSingleton<IChuteManager>(sp =>
            new ZhiQianChuteManager(zhiQianOptions, adapter, sp.GetRequiredService<SafeExecutor>()));
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
