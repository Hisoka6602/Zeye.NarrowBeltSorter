# SignalR接入与实时状态推送实施计划

## 1. 目标与范围

本计划用于在现有架构中接入 SignalR（客户端连接不使用鉴权 token），并满足以下能力：

1. 客户端首次连接后立即获取当前全量状态快照。
2. 连接保持期间持续推送实时增量数据。
3. 覆盖 `looptrack`、`emc`、`system`、`carrier`、`orchestration` 五类实时主题。
4. 保持现有分层边界：Core 定义契约，Execution 负责采集编排，Host 与 WebHost 负责对外通信承载。

## 2. 现状依据（代码出处）

以下规划均基于当前仓库可追溯代码：

- `Zeye.NarrowBeltSorter.Core/Manager/Realtime/IDeviceRealtimePublisher.cs`：已存在“设备实时信息发布器”抽象，且明确不绑定具体传输实现。
- `Zeye.NarrowBeltSorter.Core/Models/Realtime/DeviceRealtimeSnapshot.cs`：已定义通用实时快照模型（包含本地时间语义时间戳与 Metrics）。
- `Zeye.NarrowBeltSorter.Core/Enums/Realtime/DeviceRealtimeMessageKind.cs`：已定义 `Track/Sensor/Chute/Carrier/Drive/System` 消息类型。
- `Zeye.NarrowBeltSorter.Host/Program.cs`：当前 Host 仍为通用宿主编排入口，尚未注册 SignalR 服务与 Hub 路由。
- `Zeye.NarrowBeltSorter.Execution/Services/*`：现有编排服务已持续产出各域运行状态，具备实时采集基础。

## 3. 总体落地方案

### 3.0 承载路径与部署影响（需先决策）

当前 `Zeye.NarrowBeltSorter.Host/Program.cs` 使用 `Host.CreateApplicationBuilder`（Worker/Generic Host）且未启用 Web 路由映射，SignalR 落地需先在以下两条路径二选一：

1. 路径A（同进程承载）：将当前 Host 改造为 WebApplication 或补充 WebHost 启动链（可映射 Hub 路由）。
2. 路径B（独立进程承载）：新增独立 Web Host 进程，仅负责 SignalR Hub，对接 Core/Execution 的实时发布契约。

推荐顺序：

1. 短期优先路径B（对当前 Worker 主流程入侵更小，回滚边界清晰）。
2. 中长期评估路径A（部署拓扑更简单，但改造面更大）。

对 WindowsService/Systemd 的影响：

1. 路径A：现有服务单位保持单进程，但需增加 Web 端口、反向代理与监听配置。
2. 路径B：需要新增一个服务单位（Windows Service 或 systemd service），并维护进程间版本一致性与启动顺序。
3. 两条路径均需在发布脚本与运维文档中增加端口、探活、重启策略与资源配额说明。

### 3.1 对外通信层

在 Host 层新增 SignalR Hub（建议命名：`RuntimeRealtimeHub`），统一提供：

1. 连接建立回调：触发“当前全量状态”首帧下发。
2. 分组订阅能力：支持按主题加入分组（`looptrack`、`emc`、`system`、`carrier`、`orchestration`）。
3. 广播下发能力：由发布器将增量消息推送到对应分组。

### 3.2 快照与增量协同

新增“实时快照缓存服务”（Execution 层），职责：

1. 订阅并收敛各域当前状态，维护内存快照。
2. 新客户端连接时生成一次性全量快照响应。
3. 各域状态变化时输出增量快照，交给发布器推送。

### 3.3 发布器实现

在 Infrastructure/Host 侧新增 `IDeviceRealtimePublisher` 的 SignalR 实现，职责：

1. `PublishAsync`：单条实时消息路由到对应主题分组。
2. `PublishBatchAsync`：批量消息聚合发送，降低调用开销。
3. 异常路径统一记录 NLog，并通过 `Faulted` 事件上抛。

### 3.4 五类主题映射

建议统一主题键（小写）与数据范围：

1. `looptrack`：目标速度、实际速度、稳速状态、连接状态、感应区小车、上车位小车。
2. `emc`：IO 触发、IO 当前状态、按钮按下/释放、监控故障。
3. `system`：系统状态、模式信息、关键开关状态。
4. `carrier`：小车连接状态、载货状态、方向、速度、环信息。
5. `orchestration`：分拣任务关键实时信息（成熟、上车、落格、异常摘要）。

### 3.5 无 token 接入的安全边界（强制前置）

SignalR 连接“不使用 token”仅表示 Hub 层不做令牌鉴权，不代表可直接公网暴露。上线必须同时满足以下边界：

1. 网络边界：仅允许内网可达，禁止公网直接访问 Hub 端口。
2. 入口边界：反向代理层启用访问控制（至少满足 IP 白名单，建议叠加统一网关鉴权）。
3. 功能开关：默认关闭 SignalR（建议配置 `Realtime:SignalR:Enabled=false`），仅在目标环境显式开启。
4. 暴露面检查：上线验收前执行端口扫描、代理路由白名单核对、未授权访问回归验证。
5. 审计要求：连接建立、订阅主题、发送失败、异常断连均需落盘日志并可追溯。

## 4. 分阶段实施计划（3个PR）

## PR-1：建立 SignalR 通道骨架与连接首帧

交付项：

1. Host 注册 SignalR 服务并映射 Hub 路由（示例：`/hubs/realtime`）。
2. 新增 Hub 连接回调与主题分组加入接口（无鉴权 token）。
3. 新增快照缓存读取接口，连接后立即回传全量首帧。
4. 增加基础日志与异常隔离，确保连接异常不会影响主流程。

验收标准：

- 客户端可连接 Hub（无需 token）。
- 连接成功后在限定时间内收到全量首帧。
- 首帧包含五类主题的当前状态（若某类暂无数据则返回空集合而非错误）。

## PR-2：增量实时推送与发布器打通

交付项：

1. 实现 `IDeviceRealtimePublisher` 的 SignalR 版本。
2. 将 looptrack、emc、system、carrier、orchestration 事件接入增量发布链路。
3. 支持按主题分组推送，减少无关消息广播。
4. 为批量推送增加基础限流与背压策略（仅内存队列，不阻塞发布线程）。

验收标准：

- 各域状态变化后客户端可实时收到增量消息。
- 任一订阅者异常不阻塞其他订阅者与发布线程。
- 高峰时段无明显消息堆积导致的主流程阻塞。

## PR-3：稳定性、观测与联调文档

交付项：

1. 补齐连接中断重连后“重新下发全量首帧 + 持续增量”的一致性流程。
2. 增加关键日志分类与指标观测点（连接数、发送失败数、消息堆积长度）。
3. 完成客户端联调手册（订阅流程、重连策略、消息结构说明）。
4. 补齐测试（至少覆盖连接首帧、主题订阅、增量发布、重连恢复）。

验收标准：

- 客户端重连后可自动恢复订阅并收到最新全量快照。
- 异常链路均有可检索日志落盘。
- 回归测试通过且不破坏现有编排功能。

## 5. 客户端连接说明（无需鉴权 token）

## 5.1 JavaScript 客户端

```javascript
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
  .withUrl("http://127.0.0.1:5000/hubs/realtime")
  .withAutomaticReconnect()
  .build();

connection.on("RealtimeSnapshot", (payload) => {
  console.log("全量首帧", payload);
});

connection.on("RealtimeDelta", (payload) => {
  console.log("实时增量", payload);
});

await connection.start();
await connection.invoke("SubscribeTopics", ["looptrack", "emc", "system", "carrier", "orchestration"]);
```

说明：

1. 不传 token，直接通过 Hub 地址连接。
2. `RealtimeSnapshot` 用于连接成功后的全量状态。
3. `RealtimeDelta` 用于后续实时增量。
4. 主题订阅建议在 `start()` 成功后立即调用。

## 5.2 .NET 客户端

```csharp
using Microsoft.AspNetCore.SignalR.Client;

var connection = new HubConnectionBuilder()
    .WithUrl("http://127.0.0.1:5000/hubs/realtime")
    .WithAutomaticReconnect()
    .Build();

connection.On<object>("RealtimeSnapshot", payload => Console.WriteLine($"全量首帧: {payload}"));
connection.On<object>("RealtimeDelta", payload => Console.WriteLine($"实时增量: {payload}"));

await connection.StartAsync();
await connection.InvokeAsync("SubscribeTopics", new[] { "looptrack", "emc", "system", "carrier", "orchestration" });
```

## 6. 验收清单（Checklist）

- [ ] 已完成承载路径决策（同进程 WebHost 或独立 Web Host）
- [ ] 客户端可无 token 连接 SignalR Hub
- [ ] SignalR 默认关闭且仅允许内网访问
- [ ] 反向代理/IP 白名单策略已生效
- [ ] 连接后立即收到当前全量状态
- [ ] 五类主题均支持增量实时推送
- [ ] 订阅者并行下发且不阻塞发布链路
- [ ] 异常路径全部记录日志并落盘
- [ ] 重连后可恢复全量+增量一致性
