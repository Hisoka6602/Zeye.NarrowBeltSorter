# WheelDiverterSorter（OnLine-Setting）上游通信与目标格口实施计划

> 目标仓库：`https://github.com/Hisoka6602/WheelDiverterSorter/tree/OnLine-Setting`

## 1. 现状分析（基于 OnLine-Setting 分支代码）

### 1.1 如何与上游通信并获取目标格口

- 上游通信核心由 `WheelDiverterSorter.Ingress/UpstreamRouting.cs` 实现，统一承载收发、连接状态、重连与超时监控。
- 接收链路采用 TCP 文本报文，按换行符 `\n` 作为报文分隔（`TerminatorPackageAdapter("\n")`）。
- 收到报文后按 JSON 解析为 `ChuteAssignmentInfo`，核心字段：
  - `ParcelId`：包裹标识
  - `ChuteId`：目标格口
  - `AssignedAt`：上游分配时间
  - `DwsPayload`：可选 DWS 数据
- 解析成功后触发 `ChuteAssignedReceived` 事件；`ParcelHostedService` 订阅该事件并更新包裹目标格口，驱动后续分拣任务编排与执行。

### 1.2 当前反馈给上游的信息

- 通过 `IUpstreamRouting` 对外有三类发送能力：
  - `SendCreateParcelAsync`（Type=`ParcelDetected`）：包裹创建通知
  - `SendDropToChuteAsync`（Type=`SortingCompleted`）：落格完成通知
  - `SendParcelExceptionAsync`（Type=`ParcelException`）：异常通知
- 在现有托管链路中，`UpstreamRoutingHostedService` 已落地前两类反馈：
  - 订阅 `ParcelCreated` 后发送 `UpstreamCreateParcelRequest`
  - 订阅 `ParcelDropped` 后发送 `SortingCompletedMessage`
- `ParcelException` 发送接口已实现于 `UpstreamRouting`，但当前主链路未见对应托管服务调用落地（处于“能力已具备、接入待补齐”状态）。

### 1.3 如何创建与维持上游连接

- 启动入口：`UpstreamRoutingHostedService` 在启动后延迟约 15 秒，调用 `ConnectAsync(UpstreamRoutingConnectionOptions)` 建链。
- 连接参数来源：`appsettings*.json` 的 `UpstreamRoutingConnectionOptions`，核心包含：
  - `Endpoint` / `Port`
  - `Mode`（`Client` 或 `Server`）
  - `ConnectTimeoutMs` / `ReceiveTimeoutMs` / `SendTimeoutMs`
  - 自动重连参数（最小/最大延迟、退避因子）
- 模式行为：
  - `Client`：主动连接上游，断线后按指数退避重连
  - `Server`：本机监听，等待上游连接；可维护活跃会话并支持广播发送
- 稳定性机制：
  - 应用层接收超时巡检（watchdog）
  - 发送超时保护
  - 异常事件 `Faulted`、断连事件 `Disconnected`、连接事件 `Connected`
  - 连接资源清理与会话回收（避免幽灵连接）

## 2. 结论摘要

- **目标格口来源**：来自上游下发 JSON 报文，字段 `ChuteId`。
- **上游反馈内容**：已稳定反馈“包裹创建”“落格完成”；“包裹异常”接口已预留但未完成主链路接入。
- **连接方式**：支持 Client/Server 双模式，采用 TouchSocket TCP + 行分隔协议 + 超时/重连/故障隔离机制。

## 3. 实施计划（面向本仓库对标落地）

> 约束：本次接入仅用于获取目标格口与反馈分拣信息，必须保持现有分拣逻辑与业务流程不变。

### 3.1 第一阶段：契约与配置对齐

- 在 Core 层固定上游通信契约：连接选项、事件载荷、收发接口、连接状态枚举。
- 将上游连接配置收口到统一 Options，并明确 Client/Server 两种模式下的配置约束与默认值。
- 对齐“目标格口下发消息”的最小字段集与可选扩展字段，保证解析兼容性与新旧版本双向兼容。

### 3.2 第二阶段：连接层与收发层实现

- 在 Ingress 层实现统一上游路由组件，支持：
  - 双模式建连
  - 行分隔报文解析
  - 连接/断开/故障事件
  - 发送超时、接收超时、自动重连
- 统一日志结构化字段，确保“收报文、发报文、异常、重连”可追踪。
- 将解析成功的目标格口消息发布为事件，不在连接层耦合业务决策。

### 3.3 第三阶段：分拣任务编排接入

- 在包裹托管服务中订阅上游目标格口事件，仅按包裹 ID 执行“目标格口字段更新与信息补全”，不调整既有分拣判定、调度策略与状态流转逻辑。
- 对“晚到路由”场景实施显式日志与保护策略，避免队列错位。
- 将“包裹创建、落格完成、包裹异常”三类上报全部接入到托管服务主链路。

### 3.4 第四阶段：验收与压测

- 功能验收：
  - 正常收取目标格口并完成分拣
  - 三类上报报文格式与字段齐全
  - Client/Server 两种连接模式均可运行
- 稳定性验收：
  - 上游断开后自动恢复
  - 接收超时与发送超时均有日志与故障事件
  - 高频报文下不出现线程阻塞与明显积压
- 联调验收：
  - 明确上游 Type 字符串与字段命名约定
  - 完成异常场景（无效格口、晚到分配、找不到包裹）联调脚本

## 4. 待确认项（实施前必须确认）

- 上游是否要求固定消息外层 Envelope（即统一消息封装结构，除 `Type` 外是否需要版本号、站点号、签名字段）。
- `ParcelException` 的触发口径与字段规范（哪些异常必须上报、是否需要错误码）。
- 上游在 Server 模式下是否允许多客户端并发接入，若允许是否采用“活跃会话优先 + 广播兜底”策略。
- 上游时间字段是否统一接受本地时间语义及格式（避免解析歧义）。

## 5. 关键代码依据（OnLine-Setting）

- `WheelDiverterSorter.Ingress/UpstreamRouting.cs`
- `WheelDiverterSorter.Core/IUpstreamRouting.cs`
- `WheelDiverterSorter.Core/Options/Upstream/UpstreamRoutingConnectionOptions.cs`
- `WheelDiverterSorter.Core/Models/Parcel/ChuteAssignmentInfo.cs`
- `WheelDiverterSorter.Core/Models/Parcel/UpstreamCreateParcelRequest.cs`
- `WheelDiverterSorter.Core/Models/Parcel/SortingCompletedMessage.cs`
- `WheelDiverterSorter.Core/Models/Parcel/ParcelExceptionMessage.cs`
- `WheelDiverterSorter.Host/Services/Hosted/UpstreamRoutingHostedService.cs`
- `WheelDiverterSorter.Host/Services/Hosted/ParcelHostedService.cs`
- `WheelDiverterSorter.Host/appsettings.json`
- `WheelDiverterSorter.Host/appsettings.Development.json`
