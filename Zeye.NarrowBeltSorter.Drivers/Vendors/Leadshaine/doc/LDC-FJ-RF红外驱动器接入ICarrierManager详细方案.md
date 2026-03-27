# LDC-FJ-RF 红外驱动器接入 ICarrierManager 详细方案（待手册逐条核对版）

## 1. 文档说明与当前结论

本文目标是为后续实现 `ICarrierManager` 提供可落地的接入蓝图，覆盖：

1. 如何接入（分层与文件落点）
2. 如何连接（初始化、重连、断连）
3. 如何控制（启停、方向、速度、动作）
4. 如何判断状态（连接、运行、故障、传感器）
5. 如何触发事件（Carrier 与 Manager 事件映射）

> 重要说明：当前仓库未包含 `LDC-FJ-RF分拣专用红外驱动器使用说明书-20240618.pdf` 原文件，无法完成“逐页摘录 + 指令逐条校对”。  
> 因此本文中所有“指令值、寄存器地址、帧格式”仅提供**对接模板与字段位定义方法**，在拿到原手册后需逐项替换，并在每条参数后补充出处页码。

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
              ├─ ILdcRfClient（协议抽象）
              └─ ICarrier（单车状态对象集合）
```

分层原则：

1. `Core` 只放契约与模型，不引用厂商协议库。
2. `Drivers` 只做协议与设备调用，不做业务编排。
3. `Host/Services` 只做启动时序、配置装配、生命周期管理。

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
2. 建立物理连接（串口/TCP，待手册确认）。
3. 发送握手命令（示例：`PING` 模板，占位）。
4. 读取设备基础信息（型号、固件、地址）。
5. 启动轮询任务（状态轮询 + 心跳）。

### 5.2 重连流程

1. 标记状态为 `Connecting`。
2. 终止旧轮询任务并释放连接。
3. 按退避策略重连（推荐 0.3s/1s/2s/5s）。
4. 重连成功后重下发关键参数（速度、方向、使能状态）。

### 5.3 断连流程

1. 停止监控循环。
2. 关闭通信端口。
3. 状态置为 `Disconnected` 并发布事件。

---

## 6. 控制流程（模板 + 示例）

> 下列“帧格式、命令字、地址、校验”必须以原手册为准替换。

### 6.1 通用命令帧模板

```text
[STX][ADDR][CMD][LEN][DATA...][CRC][ETX]
```

- `ADDR`：设备站号
- `CMD`：命令字（读状态/写速度/启停/清故障）
- `DATA`：参数区（小端/大端以手册为准）
- `CRC`：校验（CRC16/异或，待手册确认）

### 6.2 示例：启停控制（模板）

```text
启动: CMD=RUN, DATA=01
停止: CMD=STOP, DATA=00
```

### 6.3 示例：方向控制（模板）

```text
左转:  CMD=DIR, DATA=01
右转:  CMD=DIR, DATA=02
```

### 6.4 示例：速度控制（模板）

```text
设速: CMD=SET_SPEED, DATA=<rawSpeed>
读速: CMD=GET_SPEED
```

---

## 7. 状态判断与故障处理

### 7.1 状态机建议

```text
Disconnected -> Connecting -> Connected -> Faulted
       ^                          |
       +----------- Reconnect ----+
```

### 7.2 故障分类建议

1. **通信类故障**：超时、校验错误、设备离线。
2. **设备类故障**：过流、堵转、温度告警、红外异常。
3. **业务类故障**：状态冲突（例如已停机却持续上报运行）。

### 7.3 处理策略建议

- 通信类：自动重连 + 指数退避。
- 设备类：先触发 `Faulted`，按策略尝试清故障命令。
- 业务类：记录告警并等待上层人工确认。

---

## 8. 代码接入建议（可直接改造）

### 8.1 建议新增文件（最小集合）

1. `Zeye.NarrowBeltSorter.Core/Options/Carrier/LdcRfOptions.cs`
2. `Zeye.NarrowBeltSorter.Core/Manager/Carrier/ILdcRfClient.cs`
3. `Zeye.NarrowBeltSorter.Drivers/Vendors/Leadshaine/LdcRfClient.cs`
4. `Zeye.NarrowBeltSorter.Drivers/Vendors/Leadshaine/LdcCarrierManager.cs`
5. `Zeye.NarrowBeltSorter.Host/Services/LdcCarrierManagerService.cs`

### 8.2 `ILdcRfClient` 建议能力

- `ValueTask<bool> ConnectAsync(...)`
- `ValueTask<bool> DisconnectAsync(...)`
- `ValueTask<LdcRfStatusSnapshot?> ReadStatusAsync(...)`
- `ValueTask<bool> WriteRunCommandAsync(...)`
- `ValueTask<bool> WriteDirectionAsync(...)`
- `ValueTask<bool> WriteSpeedAsync(...)`
- `ValueTask<bool> ClearFaultAsync(...)`

### 8.3 `LdcCarrierManager` 建议实现重点

1. 统一内存状态锁，输出快照对象。
2. 所有设备访问经 `SafeExecutor`。
3. 统一在异常路径输出 NLog 日志。
4. 对外只暴露 `ICarrierManager` 契约，不泄漏协议细节。

---

## 9. 指令对照清单（拿到手册后逐项补全）

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

---

## 10. 验收清单（Checklist）

- [ ] 能连接设备并稳定心跳 10 分钟以上
- [ ] 启停命令可控且状态反馈一致
- [ ] 方向与速度控制可控且有回读确认
- [ ] 红外状态变化可驱动 `CarrierLoadStatusChanged`
- [ ] 关键故障可上报 `Faulted` 且日志可定位
- [ ] 断连后可自动重连并恢复业务监控
- [ ] 停机时后台任务可优雅退出

---

## 11. 后续可完善点

1. 在拿到原手册后补全第 9 章所有指令字段与页码出处。
2. 补充 `LdcCarrierManager` 单元测试：连接异常、状态映射、防抖、事件重复抑制。
3. 增加现场联调脚本化检查（连通性、故障注入、恢复时间统计）。
