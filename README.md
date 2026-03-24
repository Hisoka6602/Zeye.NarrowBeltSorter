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
│   │       ├── LoopTrackLeiMaConnectionOptions.cs
│   │       └── LoopTrackServiceOptions.cs
│   ├── Servers/
│   │   ├── LogCleanupService.cs
│   │   └── LoopTrackManagerService.cs
│   ├── Program.cs
│   ├── Worker.cs
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
  - `Options/LoopTrack/LoopTrackLeiMaConnectionOptions.cs`：LoopTrack 雷码连接参数与频率/转矩上限配置模型。
  - `Options/LoopTrack/LoopTrackServiceOptions.cs`：LoopTrack 服务总配置模型（启用、自动启动、目标速度、轮询周期、连接、PID）。
  - `Servers/LogCleanupService.cs`：日志清理后台服务。
  - `Servers/LoopTrackManagerService.cs`：`ILoopTrackManager` 宿主服务，负责配置化构造、连接、自动启停设速、状态记录与安全停止释放。
  - `Program.cs`：Host 入口与 DI 注册（SafeExecutor、Options、后台服务）。
  - `appsettings*.json`：Host 配置文件，包含 Logging、LogCleanup、LoopTrack。
- `Zeye.NarrowBeltSorter.Infrastructure`：基础设施层。
- `Zeye.NarrowBeltSorter.Ingress`：入口与接入层。
- `Zeye.NarrowBeltSorter.sln`：解决方案文件。

## 本次更新内容

- 删除 `LeiMaExecutionGuard.cs`，危险调用统一由 `SafeExecutor` 执行并隔离异常。
- 保持 `LeiMaLoopTrackManager.SetTargetSpeedAsync` 设速主链路写入 `LeiMaRegisters.TorqueSetpoint(0x030A)`，并补充测试断言不写入 `F007H`。
- 新增 Host 配置模型：
  - `LoopTrackServiceOptions`；
  - `LoopTrackLeiMaConnectionOptions`。
- 新增 `LoopTrackManagerService`：
  - 从 `LoopTrack` 配置读取连接参数、PID、目标速度与轮询周期；
  - 构造 `LeiMaModbusClientAdapter + LeiMaLoopTrackManager`；
  - 启动时按配置执行 `ConnectAsync`、`StartAsync`、`SetTargetSpeedAsync`；
  - 停止时执行 `StopAsync`、`DisconnectAsync`、`DisposeAsync`，且通过 `SafeExecutor` 保护异常链路。
- 更新 Host `Program.cs` DI：
  - 注册 `SafeExecutor` 单例；
  - 绑定 `LogCleanup` 与 `LoopTrack` 配置；
  - 注册 `LoopTrackManagerService`（并保留现有 `Worker`、`LogCleanupService`）。
- 更新 `appsettings.json` 与 `appsettings.Development.json`，补齐 `LoopTrack` 全量配置节（Enabled、TrackName、AutoStart、TargetSpeedMmps、PollingIntervalMs、LeiMaConnection、Pid）。

## 后续可完善点

- 为 `LoopTrackManagerService` 增加主机关机重连、网络抖动恢复与回退策略的集成测试。
- 将 Host 状态日志增加可配置采样级别，进一步降低高频场景下日志开销。
- 根据现场标定数据补充 `MaxOutputHz` 与 `MaxTorqueRawUnit` 的推荐参数模板。
