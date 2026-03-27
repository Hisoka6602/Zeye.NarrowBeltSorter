# LDC-FJ-RF 红外驱动器接入 ICarrierManager 详细方案（开发可落地版；含手册回填位）

## 1. 文档说明与当前结论

本文目标是为后续实现 `ICarrierManager` 提供可落地的接入蓝图，覆盖：

1. 如何接入（分层与文件落点）
2. 如何连接（初始化、重连、断连）
3. 如何控制（启停、方向、速度、动作）
4. 如何判断状态（连接、运行、故障、传感器）
5. 如何触发事件（Carrier 与 Manager 事件映射）
6. 如何通过调试助手直接发送可验证报文（含帧拆解）
7. 如何把小车命令按“组合指令”交给桥接模块转发（例如智嵌模块）

> 重要说明：当前仓库未包含 `LDC-FJ-RF分拣专用红外驱动器使用说明书-20240618.pdf` 原文件，无法完成“逐页摘录 + 指令逐条校对”。  
> 本文已补充为“可直接指导开发落地”的完整流程文档，包含模块拆分、线程模型、状态机、事件映射、异常治理、联调脚本、验收矩阵。  
> 其中“命令字、寄存器地址、帧格式具体值”已预留回填位，拿到手册后按第 11 章流程可在 1 次迭代内完成替换。

### 1.1 重要边界声明（本次重梳理）

1. LDC-FJ-RF 为无线红外驱动器，项目内推荐采用“**小车业务命令 → 桥接模块组合指令 → 无线发送**”链路。  
2. 本文不再假设 `ICarrierManager` 直接持有物理链路；可由 `IZhiQianModbusClientAdapter` 所在模块或其他桥接模块承载实际发送。  
3. `ICarrierManager` 负责业务编排，`ILdcRfCommandBridge`（建议新增接口）负责把业务动作翻译为可发送帧并下发。

---

## 2. 接入前必须完成的前置准备

### 2.1 资料准备

- [ ] 获取原始手册文件：`LDC-FJ-RF分拣专用红外驱动器使用说明书-20240618.pdf`
- [ ] 获取厂商通讯示例（串口调试截图、上位机示例、默认参数）
- [ ] 获取现场点位清单（输入传感器、输出控制、报警口、使能口）
- [ ] 获取异常码清单（告警码、复位条件、不可恢复故障）

### 2.2 硬件准备

- [ ] 明确通信介质（RS485 / TCP / 其他）
- [ ] 明确布线方式（A/B 线序、终端电阻、屏蔽接地）
- [ ] 明确电源规格与共地方式
- [ ] 明确现场急停链路是否硬件直切（建议保留硬件回路）

### 2.3 软件准备

- [ ] 明确驱动层协议适配器文件位置（`Drivers/Vendors/Leadshaine/*`）
- [ ] 明确 Options 文件位置（`Core/Options/*`）
- [ ] 明确事件载荷文件位置（`Core/Events/Carrier/*`）
- [ ] 确认危险调用统一走 `SafeExecutor`，异常必须日志输出

---

## 3. 目标架构（建议）

```text
Host/Services
  └─ CarrierManagerService（编排）
      └─ ICarrierManager（业务管理）
          └─ LdcCarrierManager（实现）
              ├─ ILdcRfCommandBridge（桥接抽象）
              ├─ ILdcProtocolCodec（帧编解码）
              └─ ICarrier（单车状态对象集合）
```

分层原则：

1. `Core` 只放契约与模型，不引用厂商协议库。
2. `Drivers` 只做协议、桥接与设备调用，不做业务编排。
3. `Host/Services` 只做启动时序、配置装配、生命周期管理。

### 3.1 开发阶段建议的目录落点

```text
Zeye.NarrowBeltSorter.Core/
  ├── Manager/Carrier/
  │   ├── ILdcRfCommandBridge.cs          // 桥接抽象（组合指令转发）
  │   └── ILdcProtocolCodec.cs            // 帧编解码抽象
  ├── Options/Carrier/
  │   └── LdcRfOptions.cs                 // 配置对象（含 Validate）
  ├── Events/Carrier/
  │   ├── LdcCarrierIoChangedEventArgs.cs // 可选：IO变化事件载荷
  │   └── LdcCarrierFaultedEventArgs.cs   // 可选：设备故障事件载荷
  └── Models/Carrier/
      ├── LdcRfStatusSnapshot.cs          // 状态快照
      └── LdcCommandResult.cs             // 命令执行结果

Zeye.NarrowBeltSorter.Drivers/
  └── Vendors/Leadshaine/
      ├── LdcRfCommandBridge.cs           // 桥接实现（可依赖智嵌模块转发）
      ├── LdcProtocolCodec.cs             // 协议编解码实现
      └── LdcCarrierManager.cs            // ICarrierManager 实现

Zeye.NarrowBeltSorter.Host/
  ├── Services/
  │   └── LdcCarrierManagerService.cs     // 编排与运行态监控
  └── appsettings*.json                   // LDC 配置段（字段需中文注释）
```

### 3.2 组件职责边界（避免后续侵入）

1. `ILdcRfCommandBridge`：只负责“组合指令下发 + 回包透传”，不做业务判断。  
2. `ILdcProtocolCodec`：只负责“帧编码/帧解析”，不关心业务语义。  
3. `LdcCarrierManager`：把设备状态翻译为 `ICarrier` / `ICarrierManager` 语义。  
4. `LdcCarrierManagerService`：只负责启动、停止、重连编排，不写业务逻辑。  
5. `SafeExecutor`：统一包裹危险调用，确保异常不击穿主循环。  
6. NLog：统一记录连接、命令、故障、状态转换，不输出高频无意义日志。

---

## 4. 与 ICarrierManager 的映射策略

### 4.1 关键状态映射表

| ICarrierManager / ICarrier 字段 | LDC 驱动器来源（待手册确认） | 映射策略 |
| --- | --- | --- |
| `ConnectionStatus` | 通讯握手结果 / 心跳应答 | 心跳连续失败 N 次置为断开 |
| `Speed` | 当前速度反馈寄存器 | 原始值按比例换算为 mm/s |
| `TurnDirection` | 方向状态位 / 最近一次控制命令 | 先读状态位；无状态位则回显命令 |
| `IsLoaded` | 红外上货位/在位传感器 | 结合防抖窗口做边沿判定 |
| `CurrentInductionCarrierId` | 感应位触发序列 + 环号 | 由管理器维护滑动窗口 |
| `LoadedCarrierIds` | 所有 `IsLoaded=true` 小车 | 每次轮询生成快照集合 |

### 4.3 `ICarrierManager` 方法到设备动作映射（桥接后）

| `ICarrierManager` 方法 | 设备层动作 | 返回 false 条件 |
| --- | --- | --- |
| `ConnectAsync` | 建立桥接可用性 + 握手 + 读取初始状态 + 启动轮询 | 桥接不可用、握手失败、初始状态读取失败 |
| `DisconnectAsync` | 停轮询 + 释放桥接会话 | 释放失败或状态不允许断开 |
| `BuildRingAsync` | 扫描全车状态并建立车号映射 | 有车离线、关键状态缺失 |
| `SetDropModeAsync` | 更新管理器内部模式（不一定下发设备） | 模式非法、当前状态不允许切换 |
| `UpdateCurrentInductionCarrierAsync` | 更新管理器内部感应位车号并触发事件 | 状态不允许更新或参数越界 |

### 4.4 `ICarrier` 方法到设备动作映射

| `ICarrier` 方法 | 设备层动作 | 备注 |
| --- | --- | --- |
| `SetSpeedAsync` | 组装设速组合指令并交桥接模块发送 + 回读确认 | 速度单位换算在 Manager 侧完成 |
| `SetTurnDirectionAsync` | 组装方向组合指令并交桥接模块发送 + 回读确认 | 建议双位互斥检查 |
| `ConnectAsync` / `DisconnectAsync` | 可代理到 Manager，或单车逻辑转为 no-op | 建议统一由 Manager 管理 |
| `LoadParcelAsync` / `UnloadParcelAsync` | 更新内存状态并触发事件 | 不建议直接写底层硬件 |

### 4.2 关键事件映射表

| 事件 | 触发条件（建议） |
| --- | --- |
| `CarrierConnectionStatusChanged` | 通讯状态变化（Connected/Disconnected/Faulted） |
| `CarrierLoadStatusChanged` | 红外检测由无货->有货或有货->无货 |
| `CurrentInductionCarrierChanged` | 感应位车号变化 |
| `LoadedCarrierEnteredChuteInduction` | 载货车进入格口感应区 |
| `Faulted` | 协议异常、校验失败、超时、设备故障码上报 |

---

## 5. 连接流程（模板）

### 5.1 初始化流程

1. 读取 `LdcRfOptions` 并执行 `Validate()`。
2. 建立桥接连接（例如：智嵌 Modbus 通道可用性检测）。
3. 发送握手命令（示例：`PING` 模板，占位，由桥接模块转发）。
4. 读取设备基础信息（型号、固件、地址）。
5. 启动轮询任务（状态轮询 + 心跳）。

#### 5.1.1 初始化伪码（可直接转实现）

说明：以下伪码示例对应 `LdcCarrierManager` 实现 `ICarrierManager.ConnectAsync` 的主流程，属于简化版本，具体访问修饰符与依赖字段以实际类定义为准。

```csharp
public async ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default)
{
    if (_connectionStatus is DeviceConnectionStatus.Connected or DeviceConnectionStatus.Connecting)
    {
        return true;
    }

    return await _safeExecutor.ExecuteAsync(
        async ct =>
        {
            UpdateConnectionStatus(DeviceConnectionStatus.Connecting);

            var connected = await _bridge.ConnectAsync(ct);
            if (!connected)
            {
                UpdateConnectionStatus(DeviceConnectionStatus.Disconnected);
                return false;
            }

            var handshake = await _bridge.HandshakeAsync(ct); // 命令字待手册回填
            if (!handshake)
            {
                await _bridge.DisconnectAsync(ct);
                UpdateConnectionStatus(DeviceConnectionStatus.Disconnected);
                return false;
            }

            var snapshot = await _bridge.ReadStatusAsync(ct);
            if (snapshot is null)
            {
                await _bridge.DisconnectAsync(ct);
                UpdateConnectionStatus(DeviceConnectionStatus.Disconnected);
                return false;
            }

            ApplySnapshot(snapshot, DateTime.Now);
            StartPollingLoop();
            UpdateConnectionStatus(DeviceConnectionStatus.Connected);
            return true;
        },
        "LDC Connect",
        fallback: false,
        cancellationToken).ConfigureAwait(false);
}
```

### 5.2 重连流程

1. 标记状态为 `Connecting`。
2. 终止旧轮询任务并释放连接。
3. 按退避策略重连（推荐 0.3s/1s/2s/5s）。
4. 重连成功后重下发关键参数（速度、方向、使能状态）。

#### 5.2.1 重连策略参数建议

- `RetryBaseDelayMs`：300  
- `RetryFactor`：2  
- `RetryMaxDelayMs`：5000  
- `MaxConsecutiveFailures`：3（超过后从 `Connected` 转 `Faulted`）  
- `RecoverySuccessThreshold`：2（连续成功 2 次后从 `Faulted` 回 `Connected`）

### 5.3 断连流程

1. 停止监控循环。
2. 关闭通信端口。
3. 状态置为 `Disconnected` 并发布事件。

---

## 6. 控制流程（开发实现版 + 回填位）

> 本章强调“如何写代码”，命令具体值仍需按手册回填。

---

### 6.0 统一命令执行管道（建议）

1. 构造请求帧（编码器）  
2. 通过通信客户端发送并等待回包  
3. 校验回包长度、CRC、应答码  
4. 转换为 `LdcCommandResult`  
5. 若失败：记录日志 + 累计失败次数 + 必要时触发 `Faulted` 事件

### 6.1 通用命令帧模板

```text
[STX][ADDR][CMD][LEN][DATA...][CRC][ETX]
```

- `ADDR`：设备站号
- `CMD`：命令字（读状态/写速度/启停/清故障）
- `DATA`：参数区（小端/大端以手册为准）
- `CRC`：校验（CRC16/异或，待手册确认）

### 6.1.1 指令帧解析示例（调试助手可直接对照）

> 说明：以下为“结构示例”，`CMD` 与 `CRC` 数值需手册回填；示例仅用于说明调试助手收发格式与拆解方法。

```text
请求帧（Hex）: 68 01 A1 02 00 01 C5 16
字段拆解：
68      -> STX（帧头）
01      -> ADDR（站号=1）
A1      -> CMD（示例：启动命令，占位）
02      -> LEN（数据长度=2）
00 01   -> DATA（示例参数，占位）
C5      -> CRC（占位，需按手册算法计算）
16      -> ETX（帧尾）
```

```text
应答帧（Hex）: 68 01 A1 01 00 D2 16
字段拆解：
68      -> STX
01      -> ADDR
A1      -> CMD（与请求命令对应）
01      -> LEN
00      -> RESULT（0=成功，其他=失败码，具体以手册为准）
D2      -> CRC（占位）
16      -> ETX
```

#### 6.1.2 编码与解码接口建议

```csharp
public interface ILdcProtocolCodec
{
    // 低分配优先：通过 Span 写入目标缓冲区，避免高频命令构建产生 byte[] 堆分配
    bool TryBuildFrame(
        byte station,
        byte command,
        ReadOnlySpan<byte> payload,
        Span<byte> destination,
        out int bytesWritten);

    bool TryParseFrame(ReadOnlySpan<byte> frame, out LdcProtocolFrame parsed, out string error);
}
```

#### 6.1.3 命令执行统一入口建议

```csharp
public async ValueTask<LdcCommandResult> ExecuteCommandAsync(
    byte command,
    ReadOnlyMemory<byte> payload,
    CancellationToken cancellationToken = default)
{
    return await _safeExecutor.ExecuteAsync(
        async ct =>
        {
            Span<byte> requestBuffer = stackalloc byte[256];
            if (!_codec.TryBuildFrame(_options.StationNo, command, payload.Span, requestBuffer, out var bytesWritten))
            {
                return LdcCommandResult.Fail("请求帧构建失败");
            }

            var request = requestBuffer.Slice(0, bytesWritten).ToArray();
            var response = await _bridge.ExchangeAsync(request, ct).ConfigureAwait(false); // 桥接层需内置超时控制
            if (!_codec.TryParseFrame(response.Span, out var parsed, out var error))
            {
                return LdcCommandResult.Fail($"回包解析失败: {error}");
            }

            if (!parsed.IsSuccess)
            {
                return LdcCommandResult.Fail($"设备返回失败码: {parsed.ResultCode}");
            }

            return LdcCommandResult.Success(parsed.Payload);
        },
        "LDC ExecuteCommand",
        fallback: LdcCommandResult.Fail("命令执行失败"),
        cancellationToken).ConfigureAwait(false);
}
```

### 6.2 示例：启停控制（模板）

```text
启动: CMD=RUN, DATA=01
停止: CMD=STOP, DATA=00
```

开发要点：

1. 启动前先检查当前故障状态；故障态下必须先走清故障流程。  
2. 启停命令后必须回读运行位并进行一致性确认。  
3. 若回读与目标不一致，返回 false 并写入 `Faulted` 事件。

### 6.3 示例：方向控制（模板）

```text
左转:  CMD=DIR, DATA=01
右转:  CMD=DIR, DATA=02
```

开发要点：

1. 方向位建议采用互斥判定（Left/Right 不可同时为 High）。  
2. 方向切换前若设备要求先降速/停机，需要在此链路执行。  
3. 方向切换后必须回读确认，避免“命令成功但未生效”。

### 6.4 示例：速度控制（模板）

```text
设速: CMD=SET_SPEED, DATA=<rawSpeed>
读速: CMD=GET_SPEED
```

开发要点：

1. 统一在 Manager 侧做 mm/s 与设备原始值换算。  
2. `SetSpeedAsync` 建议包含限幅逻辑（最小、最大、斜率限制）。  
3. 写入后回读速度，若偏差连续超过阈值则触发 `SpeedNotReached` 类事件（可新增）。

### 6.5 调试助手直发指令模板（手册回填前可先走链路验证）

> 目标：先验证“桥接链路 + 收发闭环”是否畅通，再替换为手册真实命令字。  
> 建议调试助手模式：Hex 发送，接收显示 Hex，关闭自动换行。

1. **握手探活（占位）**  
   - 发送：`68 01 90 00 91 16`  
   - 预期：返回同 `CMD=90` 的应答帧，且 `RESULT=00`。

2. **读取状态（占位）**  
   - 发送：`68 01 B0 00 B1 16`  
   - 预期：返回 `LEN>0`，可解析运行位/故障位/红外位（位定义待手册回填）。

3. **启动（占位）**  
   - 发送：`68 01 A1 02 00 01 C5 16`  
   - 预期：应答成功后，读取状态时运行位为 `1`。

4. **停止（占位）**  
   - 发送：`68 01 A2 02 00 00 C5 16`  
   - 预期：应答成功后，读取状态时运行位为 `0`。

5. **清故障（占位）**  
   - 发送：`68 01 AF 01 01 B1 16`  
   - 预期：故障码清零或进入可恢复状态。

> 注意：以上 CRC 全部是占位示意，不可用于正式联机；接入前必须按手册算法重算 CRC 并回填。

### 6.6 组合指令桥接流程（对齐无线红外场景）

```text
ICarrierManager 业务动作
  -> 生成业务命令（如 SetSpeed / SetDirection / Run）
  -> ILdcProtocolCodec 编码为设备帧
  -> ILdcRfCommandBridge 组装桥接外层帧（如智嵌模块需要的Modbus/ASCII载荷）
  -> 桥接模块发送并回收响应
  -> ILdcProtocolCodec 解析内层响应帧
  -> LdcCarrierManager 更新状态并发布事件
```

桥接层最小接口建议：

```csharp
public interface ILdcRfCommandBridge
{
    ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> DisconnectAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> HandshakeAsync(CancellationToken cancellationToken = default);
    ValueTask<LdcRfStatusSnapshot?> ReadStatusAsync(CancellationToken cancellationToken = default);
    ValueTask<ReadOnlyMemory<byte>> ExchangeAsync(
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken = default);
}
```

### 6.7 智嵌桥接示例（结构示意）

> 说明：以下只描述封装顺序，具体寄存器地址、功能码、字节序需按智嵌文档与 LDC 手册回填。

```text
LDC内层帧:        [68][01][A1][02][00][01][CRC][16]
智嵌外层载荷:      [功能码][目标通道][长度][LDC内层帧...]
最终下发到智嵌:    WriteMultipleRegisters/ASCII批量命令
```

关键点：

1. 外层桥接仅做“透明透传 + 路由”，不改写 LDC 业务语义。  
2. 内层帧编码与解析统一走 `ILdcProtocolCodec`，避免多处重复实现。  
3. 桥接超时与重试策略在桥接层处理，业务层只拿统一结果。

---

## 7. 状态判断与故障处理

### 7.1 状态机建议

```text
Disconnected -> Connecting -> Connected -> Faulted
       ^                          |
       +----------- Reconnect --->+
```

### 7.2 故障分类建议

1. **通信类故障**：超时、校验错误、设备离线。
2. **设备类故障**：过流、堵转、温度告警、红外异常。
3. **业务类故障**：状态冲突（例如已停机却持续上报运行）。

### 7.3 处理策略建议

- 通信类：自动重连 + 指数退避。
- 设备类：先触发 `Faulted`，按策略尝试清故障命令。
- 业务类：记录告警并等待上层人工确认。

### 7.4 轮询线程模型建议（可直接照搬）

```text
PollingLoop (单后台任务, 周期 20~50ms)
  ├─ ReadStatusAsync()          // 回读运行态、速度、方向、故障、红外输入
  ├─ ApplySnapshot()            // 仅在变化时更新内存状态
  ├─ RaiseEventsIfChanged()     // 发布 Carrier/Manager 事件
  ├─ UpdateHealthCounters()     // 维护连续失败计数
  └─ TryReconnectIfNeeded()     // 进入 Faulted 后按策略重连
```

线程安全建议：

1. `_stateLock`：保护纯内存状态（连接状态、车列表、映射表）。  
2. `_writeLock`：只用于写命令串行化，避免并发写冲突。  
3. 事件发布采用“先复制委托再调用”模式，避免并发订阅问题。

### 7.5 防抖与边沿检测建议

1. 红外输入位变更先记录 `LastChangedAt`。  
2. 在 `DebounceWindowMs` 内重复变更不触发业务事件。  
3. 仅在“稳定后状态”上报 `CarrierLoadStatusChanged`。

---

## 8. 代码接入建议（可直接改造）

### 8.1 建议新增文件（最小集合）

1. `Zeye.NarrowBeltSorter.Core/Options/Carrier/LdcRfOptions.cs`
2. `Zeye.NarrowBeltSorter.Core/Manager/Carrier/ILdcRfCommandBridge.cs`
3. `Zeye.NarrowBeltSorter.Core/Manager/Carrier/ILdcProtocolCodec.cs`
4. `Zeye.NarrowBeltSorter.Drivers/Vendors/Leadshaine/LdcRfCommandBridge.cs`
5. `Zeye.NarrowBeltSorter.Drivers/Vendors/Leadshaine/LdcProtocolCodec.cs`
6. `Zeye.NarrowBeltSorter.Drivers/Vendors/Leadshaine/LdcCarrierManager.cs`
7. `Zeye.NarrowBeltSorter.Host/Services/LdcCarrierManagerService.cs`

### 8.2 `ILdcRfCommandBridge` 建议能力

- `ValueTask<bool> ConnectAsync(...)`
- `ValueTask<bool> DisconnectAsync(...)`
- `ValueTask<bool> HandshakeAsync(...)`
- `ValueTask<LdcRfStatusSnapshot?> ReadStatusAsync(...)`
- `ValueTask<ReadOnlyMemory<byte>> ExchangeAsync(...)`

### 8.3 `LdcCarrierManager` 主循环结构建议

说明：`PeriodicTimer` 需要 .NET 6.0+；当前仓库目标框架为 .NET 8，可直接使用。若迁移到更低版本，可替换为 `Task.Delay` 循环或 `System.Threading.Timer`。

```csharp
private async Task PollingLoopAsync(CancellationToken cancellationToken)
{
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.PollIntervalMs));
    while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
    {
        var (ok, snapshot) = await _safeExecutor.ExecuteAsync(
            ct => _bridge.ReadStatusAsync(ct),
            "LDC ReadStatus",
            fallback: null,
            cancellationToken).ConfigureAwait(false);

        if (!ok || snapshot is null)
        {
            HandlePollFailure(DateTime.Now);
            continue;
        }

        HandlePollSuccess(snapshot, DateTime.Now);
    }
}
```

### 8.4 `LdcCarrierManager` 事件触发顺序建议

1. 先更新内存状态  
2. 再触发单车事件（`Carrier*Changed`）  
3. 最后触发管理器聚合事件（如 `LoadedCarrierEnteredChuteInduction`）  
4. 任一事件处理器抛异常时，仅记录日志，不中断主循环

### 8.5 `LdcCarrierManager` 建议实现重点

1. 统一内存状态锁，输出快照对象。
2. 所有设备访问经 `SafeExecutor`。
3. 统一在异常路径输出 NLog 日志。
4. 对外只暴露 `ICarrierManager` 契约，不泄漏协议细节。

### 8.6 Host 注入建议（示例）

```csharp
builder.Services
    .AddOptions<LdcRfOptions>()
    .Bind(builder.Configuration.GetSection("Carrier:LdcRf"))
    .ValidateOnStart();

builder.Services.AddSingleton<ILdcProtocolCodec, LdcProtocolCodec>();
builder.Services.AddSingleton<ILdcRfCommandBridge, LdcRfCommandBridge>();
builder.Services.AddSingleton<ICarrierManager, LdcCarrierManager>();
builder.Services.AddHostedService<LdcCarrierManagerService>();
```

### 8.7 appsettings 配置建议（字段均需中文注释）

```json
{
  "Carrier": {
    "LdcRf": {
      "Enabled": false,                 // 是否启用 LDC-RF 驱动器
      "Transport": "SerialRtu",         // 传输方式（示例：SerialRtu / TcpGateway）
      "SerialPortName": "COM3",         // 串口名称
      "BaudRate": 115200,               // 串口波特率
      "DataBits": 8,                    // 串口数据位
      "StopBits": 1,                    // 串口停止位
      "Parity": "None",                 // 串口校验位
      "StationNo": 1,                   // 设备站号
      "PollIntervalMs": 20,             // 状态轮询周期（毫秒）
      "ResponseTimeoutMs": 200,         // 单次命令响应超时（毫秒）
      "ReconnectBaseDelayMs": 300,      // 重连基础延迟（毫秒）
      "ReconnectMaxDelayMs": 5000,      // 重连最大延迟（毫秒）
      "MaxConsecutiveFailures": 3       // 连续失败阈值（超过进入故障态）
    }
  }
}
```

---

## 9. 联调步骤（可直接执行）

### 9.1 台架联调步骤

1. 仅接 1 台驱动器 + 1 个桥接模块（例如智嵌），启用最小配置，确认可连通。  
2. 执行“读状态”命令，确认回包长度、校验、状态位解析。  
3. 执行“启停”并观察运行位回读一致性。  
4. 执行“方向切换”并观察方向位互斥。  
5. 执行“设速+回读”，验证速度误差在阈值内。  
6. 断开通信线缆，确认进入 `Faulted` 并自动重连。  
7. 恢复通信后确认状态恢复 `Connected`，且业务循环继续。

### 9.2 线上联调步骤

1. 配置真实站号与设备参数。  
2. 先只开状态轮询，不开控制命令。  
3. 观察 30 分钟状态稳定性与日志量。  
4. 逐步开启控制命令（启停 -> 方向 -> 速度）。  
5. 引入真实红外触发，验证 `CarrierLoadStatusChanged`。  
6. 联动上游/下游系统，验证 `CurrentInductionCarrierChanged` 与落格事件。

---

## 10. 指令对照清单（拿到手册后逐项补全）

| 类别 | 指令名称 | 命令字 | 参数说明 | 响应说明 | 手册页码 |
| --- | --- | --- | --- | --- | --- |
| 握手 | 读取设备信息 | 待填 | 待填 | 待填 | 待填 |
| 控制 | 启动 | 待填 | 待填 | 待填 | 待填 |
| 控制 | 停止 | 待填 | 待填 | 待填 | 待填 |
| 控制 | 设置方向 | 待填 | 待填 | 待填 | 待填 |
| 控制 | 设置速度 | 待填 | 待填 | 待填 | 待填 |
| 状态 | 读取运行状态 | 待填 | 待填 | 待填 | 待填 |
| 状态 | 读取故障码 | 待填 | 待填 | 待填 | 待填 |
| 故障 | 清故障 | 待填 | 待填 | 待填 | 待填 |

### 10.1 调试助手直发命令速查表（占位模板）

| 序号 | 目标 | Hex 模板 | 发送前替换项 | 预期响应 |
| --- | --- | --- | --- | --- |
| 1 | 握手探活 | `68 01 90 00 91 16` | 站号、CRC | RESULT=00 |
| 2 | 读取状态 | `68 01 B0 00 B1 16` | 站号、CRC | 返回状态数据区 |
| 3 | 启动 | `68 01 A1 02 00 01 C5 16` | 参数、CRC | 运行位=1 |
| 4 | 停止 | `68 01 A2 02 00 00 C5 16` | 参数、CRC | 运行位=0 |
| 5 | 清故障 | `68 01 AF 01 01 B1 16` | 参数、CRC | 故障清除/可恢复 |

> 备注：该速查表用于联调前期链路验证，正式联机前必须由手册回填真实命令字与 CRC 算法。

---

## 11. 手册回填执行步骤（确保不是空模板）

1. 先把手册里的“通讯章节”按页码拆成：链路参数、帧格式、命令表、异常码表。  
2. 按第 10 章逐行回填“命令字/参数/响应/页码”。  
3. 在每个命令后补充“实现方法名”与“调用时机”（例如连接阶段、轮询阶段、控制阶段）。  
4. 完成回填后执行一次台架联调，并把日志样例追加到文档附录。  
5. 若有手册歧义，新增“厂商确认项”小节，避免实现偏差。

---

## 12. 验收清单（Checklist）

- [ ] 能连接设备并稳定心跳 10 分钟以上
- [ ] 启停命令可控且状态反馈一致
- [ ] 方向与速度控制可控且有回读确认
- [ ] 红外状态变化可驱动 `CarrierLoadStatusChanged`
- [ ] 关键故障可上报 `Faulted` 且日志可定位
- [ ] 断连后可自动重连并恢复业务监控
- [ ] 停机时后台任务可优雅退出

---

## 13. 开发任务拆解（可直接开工单）

1. `Core`：新增 `LdcRfOptions`、`ILdcRfCommandBridge`、`ILdcProtocolCodec`、`LdcRfStatusSnapshot`。  
2. `Drivers`：实现 `LdcProtocolCodec`（命令管道 + 编解码）。  
3. `Drivers`：实现 `LdcRfCommandBridge`（桥接转发，支持智嵌模块）。  
4. `Drivers`：实现 `LdcCarrierManager`（状态机 + 轮询 + 事件）。  
5. `Host`：注入配置、服务注册、启动编排。  
6. `Tests`：补充连接失败、重连、状态映射、事件防抖测试。  
7. `Docs`：回填第 10 章命令细节与页码出处。

---

## 14. 后续可完善点

1. 在拿到原手册后补全第 10 章所有指令字段与页码出处。
2. 补充 `LdcCarrierManager` 单元测试：连接异常、状态映射、防抖、事件重复抑制。
3. 增加现场联调脚本化检查（连通性、故障注入、恢复时间统计）。
