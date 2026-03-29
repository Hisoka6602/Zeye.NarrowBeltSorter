# LeadshaineEmcController 对标分析与三个拉取请求落地计划

## 1. 分析目标与范围

本文基于 `Hisoka6602/WheelDiverterSorter` 仓库 `OnLine-Setting` 分支（提交：`6a5a618178bf9b3298dc4f7d4f3e1a71fabf4c71`）进行源码对标，聚焦以下范围：

1. `LeadshaineEmcController` 如何实现 `IEmcController` 的初始化、监控快照、写入与重连。
2. `LeadshaineEmcController` 与 `LeadshaineIoPanel`、`LeadshaineSensor`、`IoMonitoringHostedService` 的协同关系。
3. 输出可在当前仓库以 **3 个拉取请求**逐步落地的实施计划。

---

## 2. 对标源码清单（出处）

以下结论均来自 WheelDiverterSorter `OnLine-Setting` 分支对应源码文件：

- `WheelDiverterSorter.Core/IEmcController.cs`
- `WheelDiverterSorter.Drivers/Vendors/Leadshaine/LeadshaineEmcController.cs`
- `WheelDiverterSorter.Drivers/Vendors/Leadshaine/LeadshaineIoPanel.cs`
- `WheelDiverterSorter.Drivers/Vendors/Leadshaine/LeadshaineSensor.cs`
- `WheelDiverterSorter.Drivers/Vendors/Leadshaine/Options/LeadshainePointBindingOptions.cs`
- `WheelDiverterSorter.Drivers/Vendors/Leadshaine/Options/LeadshaineBitBindingOptions.cs`
- `WheelDiverterSorter.Drivers/Vendors/Leadshaine/Validators/LeadshainePointBindingOptionsValidator.cs`
- `WheelDiverterSorter.Host/Vendors/DependencyInjection/WebApplicationBuilderLeadshaineExtensions.cs`
- `WheelDiverterSorter.Host/Services/Hosted/IoMonitoringHostedService.cs`

---

## 3. LeadshaineEmcController 实现机制拆解

### 3.1 抽象契约（Core）

`IEmcController` 对外统一暴露：

- 生命周期：`InitializeAsync`、`ReconnectAsync`
- 点位管理：`SetMonitoredIoPointsAsync`
- IO 写入：`WriteIoAsync`
- 状态观测：`Status`、`FaultCode`、`MonitoredIoPoints`
- 事件：`StatusChanged`、`Faulted`、`Initialized`

该契约保证 Vendor 实现可替换，Host/业务层只依赖统一 EMC 能力。

### 3.2 配置与映射（PointId -> Binding）

Leadshaine 采用逻辑点位映射而非直接暴露物理地址：

- `LeadshainePointBindingOptions`：定义 `PointId` 与 `Binding` 的关系。
- `LeadshaineBitBindingOptions`：定义 `Area / CardNo / PortNo / BitIndex / TriggerState`。
- `LeadshainePointBindingOptionsValidator`：校验 PointId 唯一、Area 合法、BitIndex 范围（0..31）、TriggerState 合法。

该模式将业务语义与硬件地址解耦，便于现场改线与版本演进。

### 3.3 初始化与故障恢复

`LeadshaineEmcController.InitializeCoreAsync` 核心流程：

1. 设置状态 `Initializing -> Connecting`。
2. 通过 Polly 进行分段退避重试（0ms/300ms/1s/2s）。
3. 调用 `dmc_board_init_eth` 或 `dmc_board_init` 建连。
4. 调用 `nmc_get_errcode` 检查总线异常；异常时执行 `dmc_soft_reset` 与重建连接。
5. 初始化成功后启动 IO 监控循环并发布 `Initialized` 事件。

失败路径统一发布 `Faulted` 并记录日志，状态切换至 `Faulted`。

### 3.4 监控快照模型与性能策略

控制器采用“单监控循环 + 快照共享”模型：

- `SetMonitoredIoPointsAsync` 为**增量追加**（不全量替换），并构建输入点读取分组。
- 监控线程按 `(CardNo, PortNo)` 聚合后调用 `dmc_read_inport`，降低驱动调用次数。
- 快照以 `IoPointInfo[]` 发布，外部组件只读 `MonitoredIoPoints`。
- 当读取返回 `9`（断链语义）时，切换 `Disconnected` 并触发自动重连循环。

该实现具备较好的吞吐与并发隔离能力：采集端单写、消费端多读，避免多组件并发直读硬件。

### 3.5 写入路径与边界约束

`WriteIoAsync` 的关键约束：

1. 点位必须已绑定。
2. 仅允许写 `Output` 区绑定点。
3. 使用 `dmc_write_outbit` 进行位写入（高=1、低=0）。
4. 返回码异常时回填 `FaultCode` 并记录上下文日志。

读写路径解耦：读由监控循环维护快照，写按请求点位执行。

### 3.6 自动重连策略

`ReconnectLoopAsync` 采用指数退避：

- 起始 200ms，最大 5000ms，因子 1.6。
- 重连期间可按标记跳过软复位，减少重复 reset 成本。
- 成功后退出循环；取消或释放时中止并清理。

该策略把重连职责收敛到 EMC 控制器内部，避免上层重复实现重连器。

---

## 4. 与 IoPanel / Sensor / Host 的协同关系

1. `LeadshaineIoPanel` 仅读 `MonitoredIoPoints` 快照并做边沿/防抖，不直接访问硬件。
2. `LeadshaineSensor` 启动时调用 `SensorWorkflowHelper.SyncMonitoredIoPointsToEmcAsync` 同步监控点位，再做去抖与状态事件发布。
3. `IoMonitoringHostedService` 启动顺序为：延时 -> `InitializeAsync` -> 下发按钮点位 -> 启动 IoPanel/Sensor。
4. DI 由 `UseLeadshaineEmcVendor` 统一注册 validator、options 与 `IEmcController` 实现。

形成“Core 抽象 + Vendor 实现 + Host 编排”的清晰分层。

---

## 5. 对当前仓库的可迁移结论

1. 统一 EMC 快照源：所有消费者只读快照，避免多点直连设备。
2. 逻辑点位绑定先行：通过 PointId 映射管控风险。
3. 初始化/重连/故障链路一体化：异常路径统一日志与事件。
4. 监控按端口分组批量读取：减少硬件调用往返。
5. Host 仅负责生命周期编排，不侵入设备驱动细节。

---

## 6. 三个 PR 落地实施计划

### 拉取请求-1：配置与边界打底（不启用真实驱动）

目标：补齐 LeadshaineEmcController 落地所需的配置、校验、注册骨架，确保启动前可发现配置错误。

交付项：

1. Core/Host 对齐 Leadshaine EMC 所需配置入口与校验绑定。
2. Drivers 新增或补齐 `LeadshainePointBindingOptions` 及 validator（PointId 唯一、Area/BitIndex 合法）。
3. Host 注册 `UseLeadshaineEmcVendor` 的 options + validator（可先不启用真实监控逻辑）。
4. 补齐 README 与结构清单文档同步项。

验收标准：

- 构建通过；
- 配置错误在启动前被明确拦截；
- 不改变现有业务运行行为。

---

### 拉取请求-2：LeadshaineEmcController 核心能力落地

目标：实现可运行的 EMC 控制器主链路（初始化、快照轮询、写入、重连）。

交付项：

1. 实现 `InitializeAsync / ReconnectAsync / SetMonitoredIoPointsAsync / WriteIoAsync`。
2. 实现输入监控分组读取与线程安全快照发布。
3. 实现断链检测与指数退避自动重连。
4. 补齐单元测试（点位增量注册、写入边界、断链后重连恢复）。

验收标准：

- 快照可持续更新；
- 输出写入可用且边界受控；
- 异常路径全部有日志与 Faulted 事件；
- 重连成功后状态可恢复。

---

### 拉取请求-3：Host 联动与端到端稳定性

目标：完成与 IoPanel/Sensor/HostedService 的编排闭环，提升可观测性与运维稳定性。

交付项：

1. 对齐 IoMonitoringHostedService 启停顺序（EMC -> 点位下发 -> IoPanel/Sensor）。
2. 统一事件与日志观测（Initialized/StatusChanged/Faulted）。
3. 增加联调验证清单（空快照、断链、恢复、误配置场景）。
4. 完成文档验收与运行手册补充。

验收标准：

- 服务启停稳定；
- IoPanel/Sensor 可持续消费 EMC 快照；
- 关键异常场景可定位、可恢复、可复现。

---

## 7. 风险与控制

1. **点位映射错误风险**：可能导致误读误写。  
   控制：PR-1 强校验 + 启动前失败。

2. **监控并发竞争风险**：多模块直连硬件导致冲突。  
   控制：统一由 EMC 轮询，其他模块仅消费快照。

3. **重连风暴风险**：多处重连逻辑叠加。  
   控制：仅保留 EMC 内部重连循环，Host 不重复实现。

4. **异常路径不可观测风险**：异常被吞或定位困难。  
   控制：所有异常路径统一日志 + Faulted 事件。

---

## 8. 建议实施顺序

1. 先完成 PR-1（配置/校验/注册），冻结配置边界。
2. 再完成 PR-2（控制器核心），打通“可初始化、可监控、可写入、可重连”。
3. 最后完成 PR-3（Host 联动与验收），收敛端到端行为与可观测性。

按该顺序可将风险前移，在小步迭代中逐步验证现场可用性。
