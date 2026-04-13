using NLog;
using NLog.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Execution.Parcel;
using Zeye.NarrowBeltSorter.Execution.Services;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Sorting;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;
using Zeye.NarrowBeltSorter.Core.Options.Carrier;
using Zeye.NarrowBeltSorter.Core.Options.Sorting;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Options.LogCleanup;
using Zeye.NarrowBeltSorter.Execution.Services.State;
using Zeye.NarrowBeltSorter.Execution.Services.Carrier;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Host.Vendors.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

// 步骤1：配置多层 JSON 配置源（含按能力拆分的可选文件）。
builder.ConfigureConfigurationSources(args);

// 步骤2：初始化 NLog 并替换默认日志提供者。
var nlogConfigPath = Path.Combine(AppContext.BaseDirectory, "NLog.config");
LogManager.Setup().LoadConfigurationFromFile(nlogConfigPath);
builder.Logging.ClearProviders();
builder.Logging.AddNLog(new NLogProviderOptions {
    RemoveLoggerFactoryFilter = false,
    CaptureEventId = EventIdCaptureType.EventId,
    CaptureMessageParameters = true,
});

// 步骤3：注册核心基础设施与选项绑定。
builder.Services.AddSingleton<SafeExecutor>();
builder.Services.AddSingleton<LoopTrackManagerAccessor>();
builder.Services.AddSingleton<ILoopTrackManagerAccessor>(sp => sp.GetRequiredService<LoopTrackManagerAccessor>());
builder.Services.AddSingleton<ILoadingMatchRealtimeSpeedProvider>(sp => sp.GetRequiredService<LoopTrackManagerAccessor>());
builder.Services.Configure<LogCleanupSettings>(builder.Configuration.GetSection("LogCleanup"));
builder.Services.Configure<LoopTrackServiceOptions>(builder.Configuration.GetSection("LoopTrack"));
builder.Services.Configure<ChuteForcedRotationOptions>(builder.Configuration.GetSection("Chutes:ForcedRotation"));
builder.Services.Configure<ChuteDropSimulationOptions>(builder.Configuration.GetSection("Chutes:DropSimulation"));
builder.Services.Configure<LeadshaineIoLinkageOptions>(builder.Configuration.GetSection("Leadshaine:IoLinkage"));
builder.Services.Configure<CarrierManagerOptions>(builder.Configuration.GetSection("Carrier"));
builder.Services.Configure<SortingTaskTimingOptions>(builder.Configuration.GetSection("SortingTask:Timing"));

// 步骤4：注册 Leadshaine EMC 厂商核心（EMC控制器、IoPanel、Sensor、SignalTower）。
builder.AddLeadshaineEmcVendor();

// 步骤5：注册系统状态、小车与包裹管理器单例。
builder.Services.AddSingleton<ISystemStateManager, LocalSystemStateManager>();
builder.Services.AddSingleton<ICarrierManager, InfraredSensorCarrierManager>();
builder.Services.AddSingleton<IParcelManager, ParcelManager>();

// 步骤6：注册 Leadshaine 各功能托管服务（按配置开关条件注册）。
builder.AddLeadshaineIoMonitoring();
builder.AddLeadshaineIoPanelStateTransition();
builder.AddLeadshaineIoLinkage();
builder.AddLeadshaineCarrierLoopGrouping();
builder.AddLeadshaineMaintenance();

// 步骤7：注册智嵌格口及相关托管服务（按配置开关条件注册）。
builder.AddZhiQianChutes();

// 步骤8：注册分拣任务编排托管服务（按配置开关条件注册）。
builder.AddSortingTaskOrchestration();

// 步骤9：注册日志清理托管服务。
builder.Services.AddHostedService<LogCleanupHostedService>();

// 步骤10：注册环轨托管服务（HIL 上机联调或正式运行，按配置选择）。
builder.AddLoopTrack();

#if !DEBUG
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
    builder.Services.AddWindowsService();
}
else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
    builder.Services.AddSystemd();
}
#endif
var host = builder.Build();
var startupLog = LogManager.GetCurrentClassLogger();
startupLog.Info(
    "Configuration startup mode. Environment={0}, UseEnvironmentOnlyConfig={1}",
    builder.Environment.EnvironmentName,
    Environment.GetEnvironmentVariable("ZEYE_USE_ENV_ONLY_CONFIG"));

// 步骤11：统一打印“需重启生效”配置表，便于现场快速确认运行模式与生效边界。
var restartRequiredConfigRows = new (string Key, string Value)[] {
    ("LoopTrack:Enabled", builder.Configuration["LoopTrack:Enabled"] ?? string.Empty),
    ("LoopTrack:Hil:Enabled", builder.Configuration["LoopTrack:Hil:Enabled"] ?? string.Empty),
    ("Chutes:Enabled", builder.Configuration["Chutes:Enabled"] ?? string.Empty),
    ("Chutes:Vendor", builder.Configuration["Chutes:Vendor"] ?? string.Empty),
    ("Chutes:ZhiQian:Enabled", builder.Configuration["Chutes:ZhiQian:Enabled"] ?? string.Empty),
    ("Chutes:ForcedRotation:Enabled", builder.Configuration["Chutes:ForcedRotation:Enabled"] ?? string.Empty),
    ("Chutes:DropSimulation:Enabled", builder.Configuration["Chutes:DropSimulation:Enabled"] ?? string.Empty),
    ("Leadshaine:EmcConnection:Enabled", builder.Configuration["Leadshaine:EmcConnection:Enabled"] ?? string.Empty),
    ("Leadshaine:IoLinkage:Enabled", builder.Configuration["Leadshaine:IoLinkage:Enabled"] ?? string.Empty),
    ("Leadshaine:SignalTower:Enabled", builder.Configuration["Leadshaine:SignalTower:Enabled"] ?? string.Empty),
};
var restartRequiredConfigTableBuilder = new StringBuilder()
    .AppendLine("需重启生效配置总览（启动快照）")
    .AppendLine("| 配置键 | 当前值 |")
    .AppendLine("| --- | --- |");
foreach (var row in restartRequiredConfigRows) {
    var normalizedValue = string.IsNullOrWhiteSpace(row.Value) ? "(空)" : row.Value;
    restartRequiredConfigTableBuilder.Append("| ").Append(row.Key).Append(" | ").Append(normalizedValue).AppendLine(" |");
}
startupLog.Info("{0}", restartRequiredConfigTableBuilder.ToString());
host.Run();
