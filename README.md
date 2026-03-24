# Zeye.NarrowBeltSorter

## 文件树与职责说明

```text
.
├── .github/
│   ├── copilot-instructions.md
│   ├── scripts/
│   │   └── validate_copilot_rules.py
│   └── workflows/
│       └── copilot-rules-validate.yml
├── Zeye.NarrowBeltSorter.Core/
│   ├── Enums/
│   ├── Algorithms/
│   │   ├── PidController设计规划.md
│   │   ├── PidControllerOptions.cs
│   │   ├── PidControllerInput.cs
│   │   ├── PidControllerState.cs
│   │   ├── PidControllerOutput.cs
│   │   └── PidController.cs
│   ├── Events/
│   ├── Manager/
│   ├── Models/
│   ├── Options/
│   │   ├── LogCleanup/
│   │   └── TrackSegment/
│   │       ├── LoopTrackConnectionOptions.cs
│   │       └── LoopTrackPidOptions.cs
│   └── Utilities/
├── Zeye.NarrowBeltSorter.Core.Tests/
│   ├── LeiMaLoopTrackManagerTests.cs
│   ├── LeiMaModbusClientAdapterTests.cs
│   └── PidControllerTests.cs
├── Zeye.NarrowBeltSorter.Drivers/
│   ├── Class1.cs
│   └── Vendors/
│       └── LeiMa/
│           ├── ILeiMaModbusClientAdapter.cs
│           ├── LeiMaLoopTrackManager.cs
│           ├── LeiMaModbusClientAdapter.cs
│           ├── LeiMaRegisters.cs
│           ├── LeiMaSpeedConverter.cs
│           └── doc/
│               ├── 2-LM1000H 说明书.pdf
│               ├── (雷码)快速调机参数20250826.xlsx
│               ├── Class1.cs
│               ├── 雷码LM1000H说明书参数与调用逻辑梳理.md
│               └── 雷码快速调机参数变频器配置表梳理.md
├── Zeye.NarrowBeltSorter.Execution/
├── Zeye.NarrowBeltSorter.Host/
│   ├── Options/
│   │   └── LoopTrack/
│   │       ├── LoopTrackConnectRetryOptions.cs
│   │       ├── LoopTrackLeiMaConnectionOptions.cs
│   │       ├── LoopTrackLoggingOptions.cs
│   │       └── LoopTrackServiceOptions.cs
│   ├── Servers/
│   │   ├── LogCleanupService.cs
│   │   └── LoopTrackManagerService.cs
│   ├── Program.cs
│   ├── appsettings.json
│   └── appsettings.Development.json
├── Zeye.NarrowBeltSorter.Infrastructure/
├── Zeye.NarrowBeltSorter.Ingress/
└── Zeye.NarrowBeltSorter.sln
```

- `.github/copilot-instructions.md`：Copilot 代码与交付约束规则。
- `.github/scripts/validate_copilot_rules.py`：根据 `copilot-instructions.md` 编号规则执行 PR 合规校验（规则更新时同步生效）。
- `.github/workflows/copilot-rules-validate.yml`：PR 触发的 Copilot 规则校验工作流。
- `Zeye.NarrowBeltSorter.Core`：核心领域层，包含枚举、事件载荷、管理器接口、模型、选项与安全执行工具。
  - `Algorithms/PidController设计规划.md`：PID 纯计算器设计规划文档（mm/s→Hz），定义参数模型、计算流程与防积分饱和（anti-windup）策略。
  - `Algorithms/PidControllerOptions.cs`：PID 参数对象，包含系数、采样周期、限幅与滤波参数及合法性校验。
  - `Algorithms/PidControllerInput.cs`：PID 输入载荷，定义目标速度、实际速度与积分冻结标志。
  - `Algorithms/PidControllerState.cs`：PID 迭代状态，保存积分、上一帧误差与微分状态。
  - `Algorithms/PidControllerOutput.cs`：PID 输出载荷，包含命令频率、各项贡献与下一状态。
  - `Algorithms/PidController.cs`：PID 纯计算器实现，执行 mm/s→Hz 域计算、限幅与条件积分 anti-windup。
  - `Options/TrackSegment/LoopTrackConnectionOptions.cs`：环形轨道连接参数定义（从站地址、超时、重试）。
  - `Options/TrackSegment/LoopTrackPidOptions.cs`：环形轨道 PID 参数定义（Kp/Ki/Kd）。
- `Zeye.NarrowBeltSorter.Core.Tests`：核心单元测试项目。
  - `PidControllerTests.cs`：覆盖参数校验、首帧微分、输出限幅、anti-windup 与冻结积分行为。
  - `LeiMaLoopTrackManagerTests.cs`：覆盖 LeiMa 环轨管理器连接流转、速度写入换算、启停复位命令与异常隔离行为。
  - `LeiMaModbusClientAdapterTests.cs`：覆盖 LeiMa Modbus 适配器构造参数边界校验。
- `Zeye.NarrowBeltSorter.Drivers`：设备驱动与厂商资料。
  - `Class1.cs`：Drivers 工程占位类型。
  - `Vendors/LeiMa/ILeiMaModbusClientAdapter.cs`：雷码 Modbus 读写抽象接口。
  - `Vendors/LeiMa/LeiMaLoopTrackManager.cs`：`ILoopTrackManager` 的雷码 LM1000H 实现（连接、启停、设速、告警清除、轮询与事件发布），设速主链路固定写入 `P3.10(030AH)`。
  - `Vendors/LeiMa/LeiMaModbusClientAdapter.cs`：雷码 Modbus TCP 适配器实现（TouchSocket + TouchSocket.Modbus + Polly 重试）。
  - `Vendors/LeiMa/LeiMaRegisters.cs`：雷码寄存器与命令常量（`2000H/3000H/3100H/F007H/030AH/501AH`），其中 `F007H` 仅保留扩展用途，不作为设速主链路。
  - `Vendors/LeiMa/LeiMaSpeedConverter.cs`：`mm/s <-> Hz` 与 `P3.10` 转矩原始值换算工具。
  - `Vendors/LeiMa/doc/2-LM1000H 说明书.pdf`：雷码 LM1000H 原始说明书。
  - `Vendors/LeiMa/doc/(雷码)快速调机参数20250826.xlsx`：雷码快速调机参数原始表。
  - `Vendors/LeiMa/doc/Class1.cs`：文档目录占位类型。
  - `Vendors/LeiMa/doc/雷码LM1000H说明书参数与调用逻辑梳理.md`：从说明书与联调项目提取的参数与调用逻辑梳理（含出处）。
  - `Vendors/LeiMa/doc/雷码快速调机参数变频器配置表梳理.md`：从调机参数表提取的变频器配置参数梳理。
- `Zeye.NarrowBeltSorter.Execution`：执行层（流程/调度相关）。
- `Zeye.NarrowBeltSorter.Host`：宿主程序与后台服务。
  - `Options/LoopTrack/LoopTrackConnectRetryOptions.cs`：LoopTrack 连接重试策略配置模型（最大次数、初始间隔、上限间隔）。
  - `Options/LoopTrack/LoopTrackLeiMaConnectionOptions.cs`：LoopTrack 雷码连接参数与频率/转矩上限配置模型。
  - `Options/LoopTrack/LoopTrackLoggingOptions.cs`：LoopTrack 状态日志配置模型（是否输出详细状态、Info/Debug 频率）。
  - `Options/LoopTrack/LoopTrackServiceOptions.cs`：LoopTrack 服务总配置模型（启用、自动启动、目标速度、轮询周期、连接、PID、重试、日志）。
  - `Servers/LogCleanupService.cs`：日志清理后台服务。
  - `Servers/LoopTrackManagerService.cs`：LoopTrack 主运行服务，负责配置校验、连接重试、自动启动设速、状态监测与幂等停机释放，危险路径统一经 SafeExecutor 隔离。
  - `Program.cs`：Host 入口与 DI 注册（SafeExecutor、Options、LogCleanupService、LoopTrackManagerService）。
  - `appsettings*.json`：Host 配置文件，包含 Logging、LogCleanup、LoopTrack（含 ConnectRetry 与 Logging 子配置）。
- `Zeye.NarrowBeltSorter.Infrastructure`：基础设施层。
- `Zeye.NarrowBeltSorter.Ingress`：入口与接入层。
- `Zeye.NarrowBeltSorter.sln`：解决方案文件。

## 本次更新内容

- 删除 `Zeye.NarrowBeltSorter.Host/Worker.cs`，并在 `Program.cs` 移除 `AddHostedService<Worker>()` 注册，Host 仅由 `LoopTrackManagerService + LogCleanupService` 承载后台职责。
- 强化 `LoopTrackManagerService` 为单一主服务：
  - 启动前执行配置校验（TrackName、连接参数、PID、目标速度、重试、日志频率）；
  - 连接流程改为配置化重试（最大次数、初始间隔、上限间隔）；
  - `AutoStart` 固化为 `Connect -> Start -> SetTargetSpeed`，任一步失败触发补偿停机与断连；
  - 周期状态日志输出连接/运行/稳速/目标速度/实时速度，Info/Debug 均按配置频率输出；
  - 停止流程幂等，执行 `StopAsync -> DisconnectAsync -> DisposeAsync`，且失败不阻断后续释放。
- 扩展 Host 配置模型并与配置文件一一映射：
  - 新增 `LoopTrackConnectRetryOptions`（`MaxAttempts`、`DelayMs`、`MaxDelayMs`）；
  - 新增 `LoopTrackLoggingOptions`（`EnableVerboseStatus`、`InfoStatusIntervalMs`、`DebugStatusIntervalMs`）；
  - `LoopTrackServiceOptions` 新增 `ConnectRetry`、`Logging` 子模型。
- 更新 `appsettings.json` 与 `appsettings.Development.json`，补齐并对齐 `LoopTrack.ConnectRetry.*` 与 `LoopTrack.Logging.*`。
- 回归确认 `LeiMaLoopTrackManager.SetTargetSpeedAsync` 主链路保持写入 `P3.10(030AH)`，未改回 `F007H`。

## 后续可完善点

- 为 `LoopTrackManagerService` 增加主机生命周期（启动/停止）级别的集成测试，覆盖网络抖动与重连场景。
- 在保持 NLog 性能约束前提下，增加状态日志采样率与字段白名单配置，进一步降低高频输出开销。
- 结合现场标定数据补充不同线体的 `TargetSpeedMmps / PID / MaxOutputHz` 配置模板。
