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

