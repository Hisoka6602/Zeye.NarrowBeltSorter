# LeadshineEmcController 定义与使用分析（含本项目完整接入步骤）

## 1. 文档目的

本文用于回答两个目标：

1. 分析 `Hisoka6602/WheelDiverterSorter` 中 `LeadshineEmcController`（文中同时兼容历史拼写 `LeadshaineEmcController`）是如何定义与使用的；
2. 给出在当前仓库 `Hisoka6602/Zeye.NarrowBeltSorter` 中完整接入该控制器并实现 IO 监控的落地步骤。

## 2. 分析来源（可核对）

以下结论均来自公开仓库代码，可逐项核对：

- `WheelDiverterSorter.Core/IEmcController.cs`
- `WheelDiverterSorter.Drivers/Vendors/Leadshine/LeadshineEmcController.cs`
- `WheelDiverterSorter.Drivers/Vendors/Leadshine/LeadshaineIoPanel.cs`
- `WheelDiverterSorter.Ingress/DefaultSensor.cs`
- `WheelDiverterSorter.Host/Servers/IoMonitoringHostedService.cs`
- `WheelDiverterSorter.Host/Program.cs`
- `WheelDiverterSorter.Host/appsettings.json`

> 说明：本文不基于二手描述，均基于上述文件中的实现行为整理。

---

## 3. WheelDiverterSorter 中是如何“定义”LeadshineEmcController 的

### 3.1 契约定义：`IEmcController`

`LeadshineEmcController` 先实现了统一接口 `IEmcController`，接口核心能力如下：

- 生命周期与状态：`Status`、`FaultCode`、`InitializeAsync`、`ReconnectAsync`
- IO 监控点集合：`MonitoredIoPoints`
- 监控点设置：`SetMonitoredIoPointsAsync(IReadOnlyList<IoPointInfo>)`
- IO 写入：`WriteIoAsync(int point, IoState state)`
- 事件：`StatusChanged`、`Faulted`、`Initialized`

这意味着其定位是“EMC 设备连接 + IO 读写 + 监控点快照提供者”。

### 3.2 具体实现：`LeadshineEmcController`

实现类位于 `Drivers/Vendors/Leadshine/LeadshineEmcController.cs`，关键设计点：

1. **底层库**：通过 `csLTDMC.LTDMC` 调用雷赛 API（如 `dmc_board_init_eth`、`dmc_read_inport`、`dmc_write_outbit`）。
2. **初始化逻辑**：
   - 支持带重试的初始化（`Polly WaitAndRetryAsync`）；
   - 检测错误码（`nmc_get_errcode`）；
   - 异常时尝试 `dmc_soft_reset` 后重连。
3. **监控模型**：
   - 内部维护 `IoPointInfo[] _monitoredIoPoints`（volatile）；
   - 后台线程按固定周期读取输入口位图（`dmc_read_inport`）；
   - 将 bit0~bit31 映射到监控点状态，更新到 `IoPointInfo.State`。
4. **写 IO**：`WriteIoAsync` 走 `dmc_write_outbit`，按点位写高低电平。
5. **故障隔离**：大部分异常会记录日志并通过 `Faulted` 事件上抛，但不直接让调用链崩溃。

### 3.3 IO 轮询实现细节（非常关键）

- 轮询周期常量为 `IoPollIntervalMs = 10`（10ms）；
- 每次轮询读取一次整口位图：`dmc_read_inport(_cardNo, 0)`；
- 再遍历当前监控点集合，按位运算提取状态：
  - `raw = (snapshot >> point) & 1u`
  - `0 => Low`，`1 => High`
- 仅当状态变化时更新，减少无效写入。

该模式属于“**批量读取 + 本地解码 + 状态变更更新**”，性能和实时性都比逐点读取更好。

---

## 4. WheelDiverterSorter 中是如何“使用”LeadshineEmcController 的

### 4.1 DI 注册方式（Host 层）

`Program.cs` 中注册：

- `IEmcController -> LeadshineEmcController`（Singleton）
- `IIoPanel -> LeadshaineIoPanel`（Singleton）
- `ISensorManager -> DefaultSensor`（Singleton）
- `IoMonitoringHostedService`（HostedService）

即：EMC 控制器作为单例基础设施，被 IO 面板监控和传感器监控共同复用。

### 4.2 启动流程（IoMonitoringHostedService）

`IoMonitoringHostedService.ExecuteAsync` 主要顺序：

1. 启动后延迟（等待工控机/控制器就绪）；
2. `await _emcController.InitializeAsync()`；
3. 组装监控点（来自配置 `IoPanelButtonOptions`，可扩展 `SensorOptions`）；
4. `await _emcController.SetMonitoredIoPointsAsync(ioPointInfos)`；
5. 启动上层监控器：
   - `_ioPanel.StartMonitoringAsync()`
   - `_sensorManager.StartMonitoringAsync()`

可见它是“先初始化控制器 -> 注册监控点 -> 再启动业务监控组件”的依赖顺序。

### 4.3 业务组件如何消费监控点

#### A. `LeadshaineIoPanel`

- 周期读取 `_emcController.MonitoredIoPoints`；
- 对配置的按钮点位做边沿检测（按下/释放）；
- 做防抖、急停锁存、事件分发、系统状态切换。

#### B. `DefaultSensor`

- 启动时会把 `SensorOptions` 同步到 `_emcController.SetMonitoredIoPointsAsync(...)`；
- 监控循环读取 `MonitoredIoPoints` 快照；
- 按点位触发 `SensorStateChanged`、`MonitoringStatusChanged`、`Faulted`。

这说明 `LeadshineEmcController` 在原项目中是 **IO 事实来源（Single Source of Truth）**：
IO 面板与传感器都依赖它读取电平。

---

## 5. 在 Zeye.NarrowBeltSorter 中完整接入的建议方案

> 目标：在当前项目中接入雷赛 EMC，做到“可初始化、可写 IO、可持续监控 IO、可被上层服务消费”。

### 5.1 接入总览（分层边界）

建议沿用当前仓库已有分层：

- `Core`：接口、模型、事件、选项（不依赖厂商 SDK）
- `Drivers/Vendors/Leadshaine`：仅放雷赛实现
- `Host/Services`：编排启动顺序和后台监控

避免将厂商 API 直接泄漏到 `Core` 或业务编排层。

### 5.2 步骤 1：确认驱动资产与目标文件

当前仓库已有：

- `Zeye.NarrowBeltSorter.Drivers/Vendors/Leadshaine/Emc/LTDMC.cs`
- `Zeye.NarrowBeltSorter.Drivers/Vendors/Leadshaine/Emc/LTDMC.dll`

下一步应在 `Leadshaine` 目录补齐控制器实现文件（示例命名）：

- `Zeye.NarrowBeltSorter.Drivers/Vendors/Leadshaine/LeadshaineEmcController.cs`

> 命名建议保持与仓库现有 `Leadshaine` 目录拼写一致，避免程序集与路径混乱。

### 5.3 步骤 2：定义或复用统一接口（Core）

如果当前仓库还没有 EMC 抽象，可新增（或对齐）接口能力，至少包含：

- `Task<bool> InitializeAsync(...)`
- `Task<bool> ReconnectAsync(...)`
- `Task<bool> WriteIoAsync(int point, IoState state, ...)`
- `ValueTask SetMonitoredIoPointsAsync(IReadOnlyList<IoPointInfo> ...)`
- `IReadOnlyList<IoPointInfo> MonitoredIoPoints { get; }`
- `Faulted/Initialized/StatusChanged` 事件

这样可确保后续 `IoPanel`、`Sensor`、`IoLinkage` 都面向接口编程。

### 5.4 步骤 3：实现控制器核心能力（Drivers）

`LeadshaineEmcController` 实现建议按以下骨架：

1. **初始化**
   - 根据配置调用 `dmc_board_init_eth` 或 `dmc_board_init`；
   - 读取 `nmc_get_errcode` 判断是否需要软复位；
   - 成功后将状态置为 Connected，并触发 `Initialized`。
2. **启动后台监控循环**
   - 使用 `PeriodicTimer(10ms)` 或配置化间隔；
   - 调用 `dmc_read_inport` 读取输入位图；
   - 按监控点集合更新状态。
3. **写 IO**
   - `dmc_write_outbit(cardNo, point, value)`；
   - 返回布尔成功状态，失败写日志并触发故障事件。
4. **停止与释放**
   - 停掉监控循环；
   - 调用 `dmc_board_close`。

### 5.5 步骤 4：接入 Host（DI + 后台服务）

在 Host 注册建议遵循：

1. `AddSingleton<IEmcController, LeadshaineEmcController>()`
2. 注册依赖 EMC 的监控服务（如 IoPanel/Sensor）
3. 由单个 HostedService 负责启动时序：
   - 初始化 EMC
   - 下发监控点
   - 启动业务监控

### 5.6 步骤 5：定义“监控点来源”与“消费链路”

建议至少有两类配置来源：

- 面板按钮点位（Start/Stop/EStop/Reset）
- 业务传感器点位（包裹创建、摆轮前点、称重入口/出口等）

并把两类点位合并后统一调用一次：

- `SetMonitoredIoPointsAsync(allPoints)`

消费链路：

- `IoPanel` 负责按钮边沿和状态机切换
- `SensorManager` 负责业务传感器事件
- `IoLinkageService` 负责系统状态到输出点联动

### 5.7 步骤 6：IO 监控实现建议（高性能 + 稳定）

1. **必须使用批量读口模式**：一次读入位图，再本地映射点位；
2. **监控点缓存用数组 + volatile**：减少锁竞争；
3. **仅在状态变化时写回**：降低热路径开销；
4. **防抖在上层做，不放在底层读口循环里**：职责清晰，易调参；
5. **异常只隔离不上抛致命崩溃**：通过日志 + Faulted 事件暴露。

### 5.8 步骤 7：上线前联调清单（建议逐项勾选）

1. 能成功初始化控制器（日志可见 cardNo、IP、result）；
2. 能持续读取输入位图（日志不出现高频异常）；
3. 按钮点位按下/释放可触发对应事件；
4. 传感器触发可进入业务流程；
5. 写 IO（灯塔/蜂鸣/联动输出）成功率稳定；
6. 拔网线/断电后可恢复或可人工重连；
7. 停机时后台线程可在超时内退出。

### 5.9 步骤 8：常见问题与处理

- **初始化返回非 0**：优先检查卡号、IP、SDK 环境、设备连通性；
- **输入一直为 0**：核对点位是否在 0~31、接线公共端是否正确；
- **状态抖动严重**：增大上层防抖窗口（DebounceWindowMs）；
- **写 IO 偶发失败**：增加重试策略并区分瞬时故障与硬故障；
- **监控点不生效**：确认是否已调用 `SetMonitoredIoPointsAsync` 且点位无重复冲突。

---

## 6. 最小实现顺序（建议按此执行）

1. 在 `Drivers/Vendors/Leadshaine` 新增控制器实现类；
2. 在 `Host` 完成 DI 注册；
3. 新增或改造一个 `IoMonitoring` 后台服务编排启动顺序；
4. 配置并下发监控点（面板 + 传感器）；
5. 打通 `IoPanel` 与 `SensorManager` 事件；
6. 验证写 IO 联动；
7. 做断连/重连与停机退出验证。

---

## 7. 建议的验收标准（接入完成定义）

满足以下条件即认为“Leadshaine EMC 已完成接入并可监控 IO”：

- [ ] 应用启动后 30 秒内，EMC 状态进入 Connected；
- [ ] 至少 1 个按钮点位和 1 个业务传感器点位可稳定上报变化；
- [ ] `WriteIoAsync` 可控制至少 1 路输出并可现场观察；
- [ ] 发生通信异常时，日志可定位且不会导致进程崩溃；
- [ ] 停机时监控线程可优雅退出，无僵死线程。

---

## 8. 可直接落地的代码模板（按本仓库目录改造）

> 说明：以下代码是“可运行骨架”，用于快速接入。  
> 接入时请按本仓库现有命名与目录边界微调，不要跨层引用厂商 SDK。

### 8.1 Core 层接口模板：`ILeadshaineEmcController`

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Events.Io;
using Zeye.NarrowBeltSorter.Core.Models;

namespace Zeye.NarrowBeltSorter.Core.Manager.Io
{
    /// <summary>
    /// 雷赛 EMC 控制器抽象。
    /// </summary>
    public interface ILeadshaineEmcController : IDisposable
    {
        /// <summary>
        /// 当前连接状态。
        /// </summary>
        DeviceConnectionStatus ConnectionStatus { get; }

        /// <summary>
        /// 当前故障码，无故障时为 null。
        /// </summary>
        int? FaultCode { get; }

        /// <summary>
        /// 当前监控点位快照。
        /// </summary>
        IReadOnlyList<IoPointInfo> MonitoredIoPoints { get; }

        /// <summary>
        /// 状态变更事件。
        /// </summary>
        event EventHandler<LeadshaineEmcStatusChangedEventArgs>? StatusChanged;

        /// <summary>
        /// 异常事件。
        /// </summary>
        event EventHandler<LeadshaineEmcFaultedEventArgs>? Faulted;

        /// <summary>
        /// 初始化控制器连接。
        /// </summary>
        ValueTask<bool> InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 重连控制器。
        /// </summary>
        ValueTask<bool> ReconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置监控点位集合。
        /// </summary>
        ValueTask SetMonitoredIoPointsAsync(
            IReadOnlyList<IoPointInfo> ioPoints,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 写入输出点位状态。
        /// </summary>
        ValueTask<bool> WriteIoAsync(
            int point,
            IoState state,
            CancellationToken cancellationToken = default);
    }
}
```

### 8.2 Core 层配置模板：`LeadshaineEmcControllerOptions`

```csharp
namespace Zeye.NarrowBeltSorter.Core.Options.Io
{
    /// <summary>
    /// 雷赛 EMC 控制器配置。
    /// </summary>
    public sealed class LeadshaineEmcControllerOptions
    {
        /// <summary>
        /// 是否使用以太网初始化。
        /// </summary>
        public bool UseEthernet { get; init; } = true;

        /// <summary>
        /// 卡号。
        /// </summary>
        public ushort CardNo { get; init; } = 8;

        /// <summary>
        /// 端口号。
        /// </summary>
        public ushort PortNo { get; init; } = 0;

        /// <summary>
        /// 控制器 IP（UseEthernet=true 时必填）。
        /// </summary>
        public string ControllerIp { get; init; } = "192.168.5.11";

        /// <summary>
        /// IO 轮询周期（毫秒）。
        /// </summary>
        public int IoPollIntervalMs { get; init; } = 10;

        /// <summary>
        /// 软复位后等待时间（毫秒）。
        /// </summary>
        public int SoftResetDelayMs { get; init; } = 500;
    }
}
```

### 8.3 Drivers 层实现骨架：`LeadshaineEmcController`

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Enums.Device;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Manager.Io;
using Zeye.NarrowBeltSorter.Core.Models;
using Zeye.NarrowBeltSorter.Core.Options.Io;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine
{
    /// <summary>
    /// 雷赛 EMC 控制器实现。
    /// </summary>
    public sealed class LeadshaineEmcController : ILeadshaineEmcController
    {
        private readonly ILogger<LeadshaineEmcController> _logger;
        private readonly LeadshaineEmcControllerOptions _options;
        private readonly object _gate = new();
        private readonly object _snapshotGate = new();

        private IoPointInfo[] _monitoredIoPoints = [];
        private uint _inputSnapshot;
        private CancellationTokenSource? _monitorCts;
        private Task? _monitorTask;

        public LeadshaineEmcController(
            IOptions<LeadshaineEmcControllerOptions> options,
            ILogger<LeadshaineEmcController> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public DeviceConnectionStatus ConnectionStatus { get; private set; } = DeviceConnectionStatus.Disconnected;
        public int? FaultCode { get; private set; }
        public IReadOnlyList<IoPointInfo> MonitoredIoPoints => _monitoredIoPoints;

        public event EventHandler<LeadshaineEmcStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<LeadshaineEmcFaultedEventArgs>? Faulted;

        /// <summary>
        /// 初始化 EMC 并启动输入轮询。
        /// </summary>
        public async ValueTask<bool> InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                ChangeStatus(DeviceConnectionStatus.Connecting);

                short code;
                if (_options.UseEthernet)
                {
                    code = LTDMC.dmc_board_init_eth(_options.CardNo, _options.ControllerIp);
                }
                else
                {
                    code = LTDMC.dmc_board_init();
                }

                if (code != 0)
                {
                    _logger.LogError("EMC 初始化失败，Code={Code}", code);
                    ChangeStatus(DeviceConnectionStatus.Faulted);
                    return false;
                }

                ushort err = 0;
                LTDMC.nmc_get_errcode(_options.CardNo, _options.PortNo, ref err);
                FaultCode = err;

                if (err != 0 && err != 45)
                {
                    _logger.LogWarning("检测到总线异常，Err={Err}，执行软复位", err);
                    LTDMC.dmc_soft_reset(_options.CardNo);
                    LTDMC.dmc_board_close();
                    await Task.Delay(_options.SoftResetDelayMs, cancellationToken).ConfigureAwait(false);
                    return await ReconnectAsync(cancellationToken).ConfigureAwait(false);
                }

                ChangeStatus(DeviceConnectionStatus.Connected);
                StartMonitorLoop();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EMC 初始化异常");
                RaiseFaulted("EMC 初始化异常", ex);
                ChangeStatus(DeviceConnectionStatus.Faulted);
                return false;
            }
        }

        public async ValueTask<bool> ReconnectAsync(CancellationToken cancellationToken = default)
        {
            StopMonitorLoop();
            LTDMC.dmc_board_close();
            ChangeStatus(DeviceConnectionStatus.Disconnected);
            return await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        public ValueTask SetMonitoredIoPointsAsync(
            IReadOnlyList<IoPointInfo> ioPoints,
            CancellationToken cancellationToken = default)
        {
            if (ioPoints is null) throw new ArgumentNullException(nameof(ioPoints));

            lock (_snapshotGate)
            {
                var dict = new Dictionary<int, IoPointInfo>(ioPoints.Count);
                foreach (var point in ioPoints)
                {
                    dict[point.Point] = point;
                }

                var next = new IoPointInfo[dict.Count];
                var index = 0;
                foreach (var value in dict.Values)
                {
                    next[index++] = value;
                }

                Array.Sort(next, static (left, right) => left.Point.CompareTo(right.Point));
                _monitoredIoPoints = next;
            }

            _logger.LogInformation("监控点位已更新，Count={Count}", _monitoredIoPoints.Length);
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> WriteIoAsync(
            int point,
            IoState state,
            CancellationToken cancellationToken = default)
        {
            if (point < 0)
            {
                _logger.LogWarning("写 IO 失败，Point 非法，Point={Point}", point);
                return ValueTask.FromResult(false);
            }

            try
            {
                var level = state == IoState.High ? (ushort)1 : (ushort)0;
                var code = LTDMC.dmc_write_outbit(_options.CardNo, (ushort)point, level);
                var success = code == 0;
                if (!success)
                {
                    _logger.LogWarning("写 IO 失败，Code={Code}, Point={Point}, State={State}", code, point, state);
                }

                return ValueTask.FromResult(success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "写 IO 异常，Point={Point}, State={State}", point, state);
                RaiseFaulted("写 IO 异常", ex);
                return ValueTask.FromResult(false);
            }
        }

        public void Dispose()
        {
            StopMonitorLoop();
            LTDMC.dmc_board_close();
        }

        /// <summary>
        /// 启动输入监控循环。
        /// </summary>
        private void StartMonitorLoop()
        {
            lock (_gate)
            {
                if (_monitorTask is { IsCompleted: false })
                {
                    return;
                }

                _monitorCts = new CancellationTokenSource();
                _monitorTask = Task.Run(() => MonitorLoopAsync(_monitorCts.Token));
            }
        }

        private void StopMonitorLoop()
        {
            lock (_gate)
            {
                if (_monitorCts is null)
                {
                    return;
                }

                _monitorCts.Cancel();
            }

            try
            {
                _monitorTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "停止监控线程异常");
            }
            finally
            {
                lock (_gate)
                {
                    _monitorCts?.Dispose();
                    _monitorCts = null;
                    _monitorTask = null;
                }
            }
        }

        /// <summary>
        /// 监控循环步骤：
        /// 1) 批量读取输入位图；
        /// 2) 映射监控点状态；
        /// 3) 仅状态变化时更新快照。
        /// </summary>
        private async Task MonitorLoopAsync(CancellationToken token)
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.IoPollIntervalMs));
                while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    var value = LTDMC.dmc_read_inport(_options.CardNo, 0);
                    _inputSnapshot = value;
                    var nowTicks = Stopwatch.GetTimestamp();

                    var points = _monitoredIoPoints;
                    for (var i = 0; i < points.Length; i++)
                    {
                        var point = points[i];
                        if (point.Point < 0 || point.Point > 31)
                        {
                            continue;
                        }

                        var raw = (_inputSnapshot >> point.Point) & 1u;
                        var state = raw == 0 ? IoState.Low : IoState.High;
                        if (point.State == state)
                        {
                            continue;
                        }

                        point.UpdateState(state, nowTicks);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("监控循环已停止");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "监控循环异常");
                RaiseFaulted("监控循环异常", ex);
                ChangeStatus(DeviceConnectionStatus.Faulted);
            }
        }

        private void ChangeStatus(DeviceConnectionStatus status)
        {
            if (ConnectionStatus == status)
            {
                return;
            }

            var previous = ConnectionStatus;
            ConnectionStatus = status;
            StatusChanged?.Invoke(this, new LeadshaineEmcStatusChangedEventArgs(previous, status, DateTime.Now));
        }

        private void RaiseFaulted(string message, Exception exception)
        {
            Faulted?.Invoke(this, new LeadshaineEmcFaultedEventArgs(message, exception, DateTime.Now));
        }
    }
}
```

### 8.4 Host 注入模板：`Program.cs`

```csharp
builder.Services
    .AddOptions<LeadshaineEmcControllerOptions>()
    .Bind(builder.Configuration.GetSection("LeadshaineEmc"))
    .ValidateOnStart();

builder.Services.AddSingleton<ILeadshaineEmcController, LeadshaineEmcController>();
builder.Services.AddHostedService<LeadshaineIoMonitoringService>();
```

### 8.5 监控服务模板：`LeadshaineIoMonitoringService`

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Manager.Io;
using Zeye.NarrowBeltSorter.Core.Models;
using Zeye.NarrowBeltSorter.Core.Options.Io;

namespace Zeye.NarrowBeltSorter.Host.Services
{
    /// <summary>
    /// 雷赛 IO 监控后台服务。
    /// </summary>
    public sealed class LeadshaineIoMonitoringService : BackgroundService
    {
        private readonly ILeadshaineEmcController _controller;
        private readonly IOptions<List<IoPanelButtonOptions>> _buttonOptions;
        private readonly IOptions<List<SensorOptions>> _sensorOptions;
        private readonly ILogger<LeadshaineIoMonitoringService> _logger;

        public LeadshaineIoMonitoringService(
            ILeadshaineEmcController controller,
            IOptions<List<IoPanelButtonOptions>> buttonOptions,
            IOptions<List<SensorOptions>> sensorOptions,
            ILogger<LeadshaineIoMonitoringService> logger)
        {
            _controller = controller;
            _buttonOptions = buttonOptions;
            _sensorOptions = sensorOptions;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!await _controller.InitializeAsync(stoppingToken).ConfigureAwait(false))
            {
                _logger.LogError("EMC 初始化失败，监控服务退出");
                return;
            }

            var monitored = new List<IoPointInfo>(_buttonOptions.Value.Count + _sensorOptions.Value.Count);
            foreach (var option in _buttonOptions.Value)
            {
                monitored.Add(new IoPointInfo
                {
                    Point = option.Point,
                    Type = option.Type,
                    Name = option.ButtonName,
                    DebounceWindowMs = option.DebounceWindowMs,
                });
            }

            foreach (var option in _sensorOptions.Value)
            {
                monitored.Add(new IoPointInfo
                {
                    Point = option.Point,
                    Type = option.Type,
                    Name = option.SensorName,
                    DebounceWindowMs = option.DebounceWindowMs,
                });
            }

            await _controller.SetMonitoredIoPointsAsync(monitored, stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("EMC 监控点初始化完成，Count={Count}", monitored.Count);

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
```

### 8.6 配置模板：`appsettings.json`

```json
{
  "LeadshaineEmc": {
    "UseEthernet": true,
    "CardNo": 8,
    "PortNo": 0,
    "ControllerIp": "192.168.5.11",
    "IoPollIntervalMs": 10,
    "SoftResetDelayMs": 500
  }
}
```

### 8.7 调试与验收代码（最小可见）

```csharp
controller.StatusChanged += (_, args) =>
{
    logger.LogInformation("EMC 状态变化：{Previous} -> {Current}，发生时间：{OccurredAt}",
        args.PreviousStatus,
        args.CurrentStatus,
        args.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
};

controller.Faulted += (_, args) =>
{
    logger.LogError(args.Exception, "EMC 故障：{Message}，发生时间：{OccurredAt}",
        args.Message,
        args.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
};

var writeOk = await controller.WriteIoAsync(5, IoState.Low, cancellationToken);
logger.LogInformation("写 IO 结果：{Result}", writeOk ? "成功" : "失败");
```

---

## 9. 代码接入时的重点检查项（避免返工）

1. **监控点位范围必须限定在 0~31**（与输入位图 bit 位一致）。  
2. **`SetMonitoredIoPointsAsync` 必须去重**（point 作为唯一键）。  
3. **线程退出必须可控**（Stop/Dispose 不可无限等待）。  
4. **异常必须日志化并事件化**（便于 Host 层告警与恢复策略接入）。  
5. **DI 生命周期保持单例**（避免同一卡号被多实例并发访问）。  
6. **时间输出统一本地时间格式**（日志可直接对齐现场问题时间线）。
