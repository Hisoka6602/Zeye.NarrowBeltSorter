using Zeye.NarrowBeltSorter.Core.Options.LogCleanup;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Host;
using Zeye.NarrowBeltSorter.Host.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Host.Servers;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<SafeExecutor>();
builder.Services.Configure<LogCleanupSettings>(builder.Configuration.GetSection("LogCleanup"));
builder.Services.Configure<LoopTrackServiceOptions>(builder.Configuration.GetSection("LoopTrack"));

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<LogCleanupService>();
builder.Services.AddHostedService<LoopTrackManagerService>();

var host = builder.Build();
host.Run();
