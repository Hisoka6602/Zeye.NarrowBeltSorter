using NLog;
using NLog.Extensions.Logging;
using Zeye.NarrowBeltSorter.Host.Services;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Options.LogCleanup;

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
