# IIoPanel 定义与联动 IO 服务两阶段实施计划（2 个 PR）

## 1. 目标与边界

本文目标：参考 `Hisoka6602/WheelDiverterSorter` 仓库 `OnLine-Setting` 分支的 IoPanel 与 Io 监控编排实现，制定当前仓库的两阶段落地计划：

1. 定义并实现 `IIoPanel`（抽象 + Leadshaine 实现）。
2. 实现联动 IO 服务（以托管服务统一编排 EMC/IoPanel/Sensor 生命周期）。

边界约束：

- 仅输出计划，不在本文阶段直接落地业务代码。
- 全部时间语义保持本地时间（Local Time）。
- 不使用 `global using`，不新增 `GlobalUsings.cs`。

---

## 2. 对标来源（出处）

以下结论来自既有对标文档中已确认的源码出处（WheelDiverterSorter `OnLine-Setting` 分支，提交 `6a5a618178bf9b3298dc4f7d4f3e1a71fabf4c71`）：

- `西门子S7实施计划（三个拉取请求落地）.md`
  - `WheelDiverterSorter.Drivers/Vendors/SiemensS7/SiemensS7IoPanel.cs`
  - `WheelDiverterSorter.Host/Services/Hosted/IoMonitoringHostedService.cs`
  - `WheelDiverterSorter.Core/IEmcController.cs`
- `LeadshaineEmcController实施计划（三个拉取请求落地）.md`
  - `WheelDiverterSorter.Drivers/Vendors/Leadshaine/LeadshaineIoPanel.cs`
  - `WheelDiverterSorter.Host/Services/Hosted/IoMonitoringHostedService.cs`
  - `WheelDiverterSorter.Drivers/Vendors/Leadshaine/LeadshaineSensor.cs`

本仓现状（用于计划落地基线）：

- 已有 `IEmcController` 抽象与 `LeadshaineEmcController` 实现。
- 已有 `LeadshaineIoPanelManager`（当前为具体类，尚未抽象为 `IIoPanel`）。
- 已有 `IoMonitoringHostedService`（当前以具体实现类型协同）。

---

## 3. 总体方案（2 个 PR）

### PR-1：定义 + 实现 IIoPanel（不改业务规则）

目标：补齐 `IIoPanel` 抽象，并在 Leadshaine 侧提供实现，保证 Host/Execution 后续可面向接口编排。

#### 3.1 计划改动

1. Core 新增 IoPanel 抽象接口（建议目录）：
   - `Zeye.NarrowBeltSorter.Core/Manager/IoPanel/IIoPanel.cs`
2. Core 补齐 IoPanel 状态与事件载荷（如缺失则新增）：
   - 事件载荷统一放 `Core/Events/IoPanel` 子目录，使用 `readonly record struct`。
3. Drivers 新增/调整 Leadshaine IoPanel 实现：
   - 以现有 `LeadshaineIoPanelManager` 逻辑为基础，抽象出 `LeadshaineIoPanel : IIoPanel`；
   - 现有 `LeadshaineIoPanelManager` 可作为内部协作组件保留或合并，避免重复实现。
4. DI 注册改为接口导向：
   - `AddSingleton<IIoPanel, LeadshaineIoPanel>()`。
5. 测试同步：
   - 为 `IIoPanel` 行为补充测试桩和核心流程测试；
   - 仅做命名与引用同步，不调整业务判定逻辑。

#### 3.2 验收标准

- `IIoPanel` 抽象可独立被 Host/Execution 引用。
- Leadshaine IoPanel 实现完成并通过构建测试。
- 无业务流程变更（只做抽象分层与引用切换）。

---

### PR-2：联动 IO 服务实现（面向接口编排）

目标：让联动 IO 服务只依赖 `IEmcController + IIoPanel + ISensorManager`，统一启动/停止顺序和故障收敛。

#### 3.3 计划改动

1. 在 Execution 层完善联动托管服务编排（建议沿用现有 `IoMonitoringHostedService`）：
   - 启动顺序：`EMC 初始化 -> 点位下发 -> IIoPanel 启动 -> Sensor 启动`；
   - 停止顺序：`IIoPanel 停止 -> Sensor 停止 -> EMC 释放`。
2. 将服务内部依赖由具体类型切换为接口：
   - `LeadshaineIoPanelManager` -> `IIoPanel`。
3. 统一异常路径日志与状态收敛：
   - 保持现有 NLog 体系；
   - 故障路径必须可观测并落盘。
4. Host/DI 同步注册与开关配置：
   - 仅保留编排职责，不在 Host 写厂商细节逻辑。
5. 测试同步：
   - 覆盖初始化失败、点位下发失败、停止链路、事件联动时序。

#### 3.4 验收标准

- 联动 IO 服务仅依赖接口协同，无厂商实现侵入。
- 启停链路、异常链路测试通过。
- 构建与既有测试通过，业务行为保持一致。

---

## 4. 交付清单（按 PR）

### PR-1 交付清单

- [x] `IIoPanel` 接口文件
- [x] IoPanel 事件载荷（若缺失）
- [x] `LeadshaineIoPanel` 实现与 DI 注册
- [x] 对应单元测试与命名/引用同步

### PR-2 交付清单

- [x] `IoMonitoringHostedService` 面向接口编排改造
- [x] 启停顺序与异常收敛完善
- [x] 联动链路测试（集成测试优先）
- [x] 文档与 README 结构说明同步

---

## 5. 风险与规避

1. **风险：接口切换导致引用面较大。**  
   规避：PR-1 先做“接口 + 适配实现”，不改业务判定；PR-2 再切编排依赖。

2. **风险：联动服务改造引入启动竞态。**  
   规避：严格使用既有稳定顺序（先 EMC，后 IoPanel/Sensor），并补充启动时序测试。

3. **风险：厂商实现重复代码扩散。**  
   规避：复用 `SensorWorkflowHelper`、现有点位校验器与 EMC 快照接口，不复制实现。

---

## 6. 待确认项（进入 PR-1 前确认）

1. `IIoPanel` 是否需要对外暴露“按钮角色（ButtonType）级别事件”，还是仅暴露统一状态变化事件。
2. `LeadshaineIoPanelManager` 最终形态是“重命名为 `LeadshaineIoPanel`”还是“保留 Manager 并由 `LeadshaineIoPanel` 组合调用”。
3. 联动 IO 服务是否保持现有类名 `IoMonitoringHostedService`，或重命名为更明确的 `IoLinkageHostedService`（纯命名，不改逻辑）。
