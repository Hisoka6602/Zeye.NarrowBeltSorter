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
- 明确互斥规则：当前“演示赋值目标格口”与“上游赋值目标格口”必须互斥，同一包裹同一时刻仅允许一个来源生效。
- 互斥开关约束：采用单一配置开关 `TargetChuteAssignmentSource`（`Demo` 或 `Upstream`）控制来源；当配置为 `Upstream` 时，演示赋值入口必须禁用；当配置为 `Demo` 时，上游赋值写入口必须禁用。
- 动态切换约束：支持运行时切换来源时，切换流程必须先清理旧来源未处理赋值上下文，再启用新来源入口，切换操作需满足幂等。
- 对“晚到路由”场景实施显式日志与保护策略，避免队列错位。
- 将“包裹创建、落格完成、包裹异常”三类上报全部接入到托管服务主链路。

### 3.4 本项目落地接口、方法与触发调用清单（详细）

> 约束重申：以下接入仅新增“目标格口获取 + 分拣结果反馈”链路，不改动既有分拣判定、调度策略、状态机与执行流程。

#### A. Core 层（先定义契约）

- `Zeye.NarrowBeltSorter.Core/Manager/Upstream/IUpstreamRoutingManager.cs`（新增接口）
  - `Task ConnectAsync(UpstreamRoutingOptions options, CancellationToken cancellationToken)`：成功表示建连完成；失败抛出异常，由调用侧统一记录并进入故障路径。
  - `Task DisconnectAsync(CancellationToken cancellationToken)`：成功表示连接资源全部释放；失败抛出异常并由调用侧执行降级收敛。
  - `Task SendParcelCreatedAsync(UpstreamParcelCreatedMessage message, CancellationToken cancellationToken)`：成功表示报文已写入发送链路；发送失败抛出异常。
  - `Task SendSortingCompletedAsync(UpstreamSortingCompletedMessage message, CancellationToken cancellationToken)`：成功表示报文已写入发送链路；发送失败抛出异常。
  - `Task SendParcelExceptionAsync(UpstreamParcelExceptionMessage message, CancellationToken cancellationToken)`：成功表示报文已写入发送链路；发送失败抛出异常。
  - 事件：
    - `TargetChuteAssigned`（上游下发目标格口）
    - `Connected` / `Disconnected` / `Faulted`（连接生命周期）
  - 事件执行约束：所有事件发布与订阅处理均通过 `SafeExecutor` 非阻塞隔离执行，订阅者并行获取，禁止阻塞发布链路。

- `Zeye.NarrowBeltSorter.Core/Options/Upstream/UpstreamRoutingOptions.cs`（新增 Options）
  - 仅承载连接参数，不承载业务策略，建议字段与范围如下：
    - `Enabled`：`true|false`，是否启用上游通信。
    - `Mode`：`Client|Server`。
    - `Endpoint`：非空主机名或 IPv4/IPv6。
    - `Port`：`1-65535`。
    - `ConnectTimeoutMs`：`100-60000`。
    - `ReceiveTimeoutMs`：`100-60000`。
    - `SendTimeoutMs`：`100-60000`。
    - `ReconnectMinDelayMs`：`100-60000`。
    - `ReconnectMaxDelayMs`：`1000-300000`，且 `>= ReconnectMinDelayMs`。
    - `ReconnectBackoffFactor`：`1.0-10.0`。
    - `MessageTerminator`：默认 `\n`。
  - 配置注释约束：`appsettings.json` 与基础配置文件中每个字段必须有中文注释，且注释写明可填写范围与枚举可选项。
  - 时间语义约束：若后续增加时间字符串字段，统一按本地时间解析，示例禁止使用时区偏移标记。

- `Zeye.NarrowBeltSorter.Core/Events/Upstream/*.cs`（新增事件载荷，使用 `readonly record struct`）
  - `UpstreamTargetChuteAssignedEventArgs`：`ParcelId`、`TargetChuteId`、`AssignedAtLocal`
  - `UpstreamConnectionStatusChangedEventArgs`
  - `UpstreamFaultedEventArgs`

- `Zeye.NarrowBeltSorter.Core/Models/Upstream/*.cs`（新增上行消息模型）
  - `UpstreamParcelCreatedMessage`
  - `UpstreamSortingCompletedMessage`
  - `UpstreamParcelExceptionMessage`

#### B. Drivers/Ingress 层（实现通信）

- `Zeye.NarrowBeltSorter.Drivers/Upstream/UpstreamRoutingManager.cs`（新增实现类）
  - 实现 `IUpstreamRoutingManager`，负责 TCP 建连、收包拆包、JSON 解析、发送、重连与故障上报。
  - 强制使用 TouchSocket 实现 TCP 读写，不引入其他 TCP 通信库。
  - `ConnectAsync` 内触发：成功建连后发布 `Connected`。
  - 接收回调内触发：解析到目标格口后发布 `TargetChuteAssigned`。
  - 异常路径触发：发布 `Faulted`，并记录 NLog 落盘日志（不得吞异常、不得只抛出不记录）。
  - 断连路径触发：发布 `Disconnected`。
  - 日志落盘约束：新增上游日志分类时，需在 NLog 中声明文件 target 与路由规则，单文件大小不超过 10MB（超限轮转）。

#### C. Execution 层（只做桥接，不改分拣逻辑）

- `Zeye.NarrowBeltSorter.Execution/Services/Hosted/UpstreamRoutingHostedService.cs`（新增托管服务）
  - `StartAsync`：读取 `UpstreamRoutingOptions`，调用 `IUpstreamRoutingManager.ConnectAsync`。
  - `StopAsync`：调用 `DisconnectAsync`。
  - 订阅 `ParcelManager`/分拣事件，桥接发送：
    - 包裹创建事件 -> `SendParcelCreatedAsync`
    - 落格完成事件 -> `SendSortingCompletedAsync`
    - 业务异常事件 -> `SendParcelExceptionAsync`
  - 托管服务内所有订阅回调必须通过 `SafeExecutor` 非阻塞隔离执行，避免阻塞发布者和其他订阅者。

- `Zeye.NarrowBeltSorter.Execution/Services/SortingTaskOrchestrationService.cs`（仅新增订阅入口）
  - 新增订阅 `TargetChuteAssigned` 的处理方法（例如 `HandleUpstreamTargetChuteAssignedAsync`）。
  - 在该处理方法与演示赋值入口之间增加互斥守卫（统一来源开关），保证单来源写入目标格口，禁止并发双来源覆盖。
  - 守卫实现细节：
    - 冲突检测时机：调用前与写入前双重校验当前来源。
    - 冲突处理策略：拒绝本次写入 + 记录告警日志（包含 `ParcelId`、请求来源、当前生效来源），不抛出中断主链路的异常。
    - 失效降级策略：若来源状态不可判定，默认拒绝写入并保持原目标格口不变，记录故障日志等待人工处置。
  - 方法内仅执行：
    - 按 `ParcelId` 查找包裹
    - 更新包裹目标格口字段/补充上游信息
    - 记录日志（命中、晚到、缺失）
  - 处理方法内潜在阻塞与异常路径统一通过 `SafeExecutor` 隔离执行并记录落盘日志。
  - 明确不执行：
    - 不改变分拣判定规则
    - 不改变调度优先级
    - 不改变状态流转与触发机制

#### D. Host 层（注册与配置）

- `Zeye.NarrowBeltSorter.Host/Vendors/DependencyInjection/HostApplicationBuilderSortingExtensions.cs`
  - 注册 `IUpstreamRoutingManager` 与 `UpstreamRoutingHostedService`。
- `Zeye.NarrowBeltSorter.Host/appsettings*.json`
  - 新增 `UpstreamRoutingOptions` 配置节（仅连接参数，保持本地时间语义）。

### 3.5 第四阶段：验收与压测

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
