using NLog;
using NLog.Extensions.Logging;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Execution.Parcel;
using Zeye.NarrowBeltSorter.Execution.Services;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;
using Zeye.NarrowBeltSorter.Core.Options.Carrier;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Options.LogCleanup;
using Zeye.NarrowBeltSorter.Execution.Services.State;
using Zeye.NarrowBeltSorter.Execution.Services.Carrier;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Host.Vendors.DependencyInjection;
using Zeye.NarrowBeltSorter.Core.Options.Chutes.Zeye.NarrowBeltSorter.Core.Options.Chutes;

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
builder.Services.Configure<LogCleanupSettings>(builder.Configuration.GetSection("LogCleanup"));
builder.Services.Configure<LoopTrackServiceOptions>(builder.Configuration.GetSection("LoopTrack"));
builder.Services.Configure<ChuteForcedRotationOptions>(builder.Configuration.GetSection("Chutes:ForcedRotation"));
builder.Services.Configure<ChuteDropSimulationOptions>(builder.Configuration.GetSection("Chutes:DropSimulation"));
builder.Services.Configure<LeadshaineIoLinkageOptions>(builder.Configuration.GetSection("Leadshaine:IoLinkage"));
builder.Services.Configure<CarrierManagerOptions>(builder.Configuration.GetSection("Carrier"));

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

// 步骤7：注册智嵌格口及相关托管服务（按配置开关条件注册）。
builder.AddZhiQianChutes();

// 步骤8：注册分拣任务编排托管服务（按配置开关条件注册）。
builder.AddSortingTaskOrchestration();

// 步骤9：注册日志清理托管服务。
builder.Services.AddHostedService<LogCleanupHostedService>();

// 步骤10：注册环轨托管服务（HIL 上机联调或正式运行，按配置选择）。
builder.AddLoopTrack();

var host = builder.Build();
var startupLog = LogManager.GetCurrentClassLogger();
startupLog.Info(
    "Configuration startup mode. Environment={0}, UseEnvironmentOnlyConfig={1}",
    builder.Environment.EnvironmentName,
    Environment.GetEnvironmentVariable("ZEYE_USE_ENV_ONLY_CONFIG"));
host.Run();

