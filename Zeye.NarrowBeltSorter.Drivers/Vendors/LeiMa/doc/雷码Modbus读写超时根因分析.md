# 雷码 Modbus 读写超时根因分析

> 本文档仅做问题分析，**不涉及任何代码修改**。  
> 涉及文件：`Zeye.NarrowBeltSorter.Drivers/Vendors/LeiMa/LeiMaModbusClientAdapter.cs`

---

## 一、硬件约束前提

轨道中无论多少个从站，**所有从站共用同一条 COM 串口总线**，Modbus RTU 协议天然串行，读写指令必须逐条串行执行。格口驱动（ZhiQian）同样如此。代码中以下三层门控均为保障串行而设计，属于**正确设计**，不应删除：

| 门控变量 | 所在位置 | 作用 |
|---|---|---|
| `_serialSharedConnection.Gate`（`SemaphoreSlim`） | `LeiMaModbusClientAdapter` | 共享串口总线级串行化 |
| `_operationGate`（`SemaphoreSlim`） | `LeiMaModbusClientAdapter` | 单适配器操作级串行化 |
| `_comIoGate`（`SemaphoreSlim`） | `LeiMaLoopTrackManager` | 管理器层全局串行化 |

---

## 二、根因分析

### 根因 1：`TimeoutStrategy.Pessimistic` 导致"幽灵帧"（**最关键**）

**出处（`LeiMaModbusClientAdapter.cs` 第 229–230 行）：**

```csharp
var timeoutPolicy = Policy.TimeoutAsync(
    TimeSpan.FromMilliseconds(_modbusTimeoutMilliseconds + 200),
    TimeoutStrategy.Pessimistic);  // ← 问题根源
_requestPolicy = Policy.WrapAsync(retryPolicy, timeoutPolicy);
```

**问题机制：**

`TimeoutStrategy.Pessimistic` 模式下，Polly 超时触发后**不会取消底层 RTU 操作**，底层 `await` 仍在后台继续等待设备响应帧。当 Polly 触发重试时：

1. 新的 Modbus 请求帧已发出
2. 上一次请求的**旧响应帧**可能在此时才到达
3. 适配器将旧帧误认为新请求的响应 → 解析失败或数据错乱
4. 再次触发超时重试，形成**级联超时**

最坏情况下实际等待时间为：
```
实际超时 = 单次超时 × 重试次数
         = (modbusTimeoutMilliseconds + 200) × retryCount
```

**建议修复：**

将 `TimeoutStrategy.Pessimistic` 改为 `TimeoutStrategy.Optimistic`，超时时通过 `CancellationToken` 真正终止底层等待，不留幽灵帧：

```csharp
// 建议改为：
var timeoutPolicy = Policy.TimeoutAsync(
    TimeSpan.FromMilliseconds(_modbusTimeoutMilliseconds + 200),
    TimeoutStrategy.Optimistic);
```

> **注意**：使用 `Optimistic` 要求底层操作正确响应 `CancellationToken`，TouchSocket.Modbus 的异步方法均支持取消令牌，适合使用此策略。

---

### 根因 2：Polly `WrapAsync` 组合顺序导致"所有重试共享一个超时"

**出处（`LeiMaModbusClientAdapter.cs` 第 230 行）：**

```csharp
_requestPolicy = Policy.WrapAsync(retryPolicy, timeoutPolicy);
//                                ↑外层         ↑内层
```

**问题机制：**

`Policy.WrapAsync(outer, inner)` 的执行顺序是：outer 先执行，inner 在 outer 内部执行。即：

```
retry（外层）
  └─ timeout（内层）包裹单次执行
```

此顺序下，**每次重试都有独立超时**，语义上是正确的。但若将顺序写反为 `WrapAsync(timeoutPolicy, retryPolicy)`，则超时将包裹整个重试过程（所有重试共享一个计时），可能导致第一次超时后后续重试无法执行。

**当前代码顺序（retry 包 timeout）是正确的**，但若将来调整顺序需注意此语义差异。

---

## 三、结论

| 项目 | 结论 |
|---|---|
| 串行设计（三层门控） | ✅ 正确，不应修改 |
| `TimeoutStrategy.Pessimistic` | ❌ 存在幽灵帧风险，建议改为 `Optimistic` |
| `WrapAsync(retry, timeout)` 顺序 | ✅ 当前顺序正确，每次重试独立超时 |

**根本原因**：`TimeoutStrategy.Pessimistic` 导致超时后底层 RTU 等待未被取消，旧响应帧残留并干扰后续重试，形成级联超时。与从站数量和并发设计无关。
