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
│   │   └── TrackSegment/
│   │       ├── ILoopTrackManager.cs
│   │       └── ILeiMaModbusClientAdapter.cs
│   ├── Models/
│   ├── Options/
│   │   ├── LogCleanup/
│   │   ├── LoopTrack/
│   │   │   ├── LoopTrackConnectRetryOptions.cs
│   │   │   ├── LoopTrackLeiMaConnectionOptions.cs
│   │   │   ├── LoopTrackLeiMaSerialRtuOptions.cs
│   │   │   ├── LoopTrackLoggingOptions.cs
│   │   │   └── LoopTrackServiceOptions.cs
│   │   └── TrackSegment/
│   │       ├── LoopTrackConnectionOptions.cs
│   │       └── LoopTrackPidOptions.cs
│   └── Utilities/
│       └── LoopTrack/
│           ├── LeiMaRegisters.cs
│           ├── LeiMaSpeedConverter.cs
│           └── LoopTrackLeiMaTransportModes.cs
├── Zeye.NarrowBeltSorter.Core.Tests/
│   ├── FakeLoopTrackManager.cs
│   ├── LeiMaLoopTrackManagerTests.cs
│   ├── LeiMaModbusClientAdapterTests.cs
│   ├── LoopTrackManagerServiceTests.cs
│   ├── TestableLoopTrackManagerService.cs
│   └── PidControllerTests.cs
├── Zeye.NarrowBeltSorter.Drivers/
│   ├── Class1.cs
│   └── Vendors/
│       └── LeiMa/
│           ├── LeiMaLoopTrackManager.cs
│           ├── LeiMaModbusClientAdapter.cs
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
│   │       ├── LoopTrackLeiMaSerialRtuOptions.cs
│   │       ├── LoopTrackLeiMaTransportModes.cs
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
  - `FakeLoopTrackManager.cs`：`ILoopTrackManager` 测试桩，覆盖连接、启停、断连与释放调用计数，支撑服务补偿链路断言。
  - `PidControllerTests.cs`：覆盖参数校验、首帧微分、输出限幅、anti-windup 与冻结积分行为。
  - `LeiMaLoopTrackManagerTests.cs`：覆盖 LeiMa 环轨管理器连接流转、速度写入换算、启停复位命令与异常隔离行为。
  - `LeiMaModbusClientAdapterTests.cs`：覆盖 LeiMa Modbus 适配器构造参数边界校验。
  - `LoopTrackManagerServiceTests.cs`：覆盖 Transport 分支（TcpGateway/SerialRtu）、SerialRtu 非法参数安全退出与 AutoStart 失败补偿链路。
  - `TestableLoopTrackManagerService.cs`：服务测试专用派生类型，暴露受保护入口并统计管理器创建次数。
- `Zeye.NarrowBeltSorter.Drivers`：设备驱动与厂商资料。
  - `Class1.cs`：Drivers 工程占位类型。
  - `Vendors/LeiMa/LeiMaLoopTrackManager.cs`：`ILoopTrackManager` 的雷码 LM1000H 实现（连接、启停、设速、告警清除、轮询与事件发布），设速主链路固定写入 `P3.10(030AH)`。
  - `Vendors/LeiMa/LeiMaModbusClientAdapter.cs`：雷码 Modbus 双模式适配器实现（TcpGateway/SerialRtu，统一 TouchSocket + TouchSocket.Modbus + Polly 重试）。
  - `Vendors/LeiMa/doc/2-LM1000H 说明书.pdf`：雷码 LM1000H 原始说明书。
  - `Vendors/LeiMa/doc/(雷码)快速调机参数20250826.xlsx`：雷码快速调机参数原始表。
  - `Vendors/LeiMa/doc/Class1.cs`：文档目录占位类型。
  - `Vendors/LeiMa/doc/雷码LM1000H说明书参数与调用逻辑梳理.md`：从说明书与联调项目提取的参数与调用逻辑梳理（含出处）。
  - `Vendors/LeiMa/doc/雷码快速调机参数变频器配置表梳理.md`：从调机参数表提取的变频器配置参数梳理。
- `Zeye.NarrowBeltSorter.Execution`：执行层（流程/调度相关）。
- `Zeye.NarrowBeltSorter.Host`：宿主程序与后台服务。
  - `Servers/LogCleanupService.cs`：日志清理后台服务。
  - `Servers/LoopTrackManagerService.cs`：LoopTrack 主运行服务，负责配置校验、连接重试、自动启动设速、闭环稳速监测、实时速度日志与 PID 调参日志，危险路径统一经 SafeExecutor 隔离。
  - `Program.cs`：Host 入口与 DI 注册（SafeExecutor、Core.Options、LogCleanupService、LoopTrackManagerService）。
  - `appsettings*.json`：Host 配置文件，所有字段均附中文注释，覆盖 Logging、LogCleanup、LoopTrack（含 PID、ConnectRetry 与日志频率子配置）。
- `Zeye.NarrowBeltSorter.Infrastructure`：基础设施层。
- `Zeye.NarrowBeltSorter.Ingress`：入口与接入层。
- `Zeye.NarrowBeltSorter.sln`：解决方案文件。

## 本次更新内容

- 新增并固化 Copilot 强制规则（31~38）：包含 appsettings 全字段中文注释、Options/interface/静态工具类目录强制、每次改动违规修复、尽量使用 var、危险代码统一 SafeExecutor、默认自动创建 PR。
- 完成目录收敛迁移：Host 中 LoopTrack Options 全量迁移至 `Zeye.NarrowBeltSorter.Core/Options/LoopTrack`；`ILeiMaModbusClientAdapter` 迁移至 `Zeye.NarrowBeltSorter.Core/Manager/TrackSegment`；`LeiMaRegisters`/`LeiMaSpeedConverter`/`LoopTrackLeiMaTransportModes` 迁移至 `Zeye.NarrowBeltSorter.Core/Utilities/LoopTrack`。
- `LeiMaLoopTrackManager` 接入 `PidController` 闭环稳速：轮询反馈速度后执行 PID 计算（含积分冻结与 anti-windup），按配置频率回写 `P3.10(030AH)` 主链路，不回退 `F007H` 主链路，并暴露 PID 状态快照。
- `LoopTrackManagerService` 增强结构化日志：新增实时速度日志（目标/实时/偏差/运行状态/稳速状态）与 PID 调参日志（P/I/D、命令值、限幅状态），均支持配置化开关与频率。
- `appsettings.json` 与 `appsettings.Development.json` 已为所有字段（含嵌套字段）补全中文注释，覆盖单位、取值范围与用途说明，保持 Host 可加载。
- 扩展 Options 字段：新增 PID 闭环参数（输出限幅、积分限幅、滤波、积分冻结）与日志频率参数、P3.10 最小写入间隔参数，并在服务层完成配置校验。
- 补充单测：新增 PID 闭环行为断言，验证 PID 状态更新且仍写入 `P3.10`，并继续断言不写 `F007H`。

## 后续可完善点

- 增加串口热插拔与串口占用冲突场景的自动恢复策略（含告警抑制与恢复窗口）。
- 增强 TCP 网关抖动恢复策略（短时抖动快速重连、长时断链分级退避）。
- 基于现场线体沉淀稳速参数模板（按负载/速度区间预置阈值与告警灵敏度）。
- 增加 `LoopTrackManagerService` 真实 IO 集成测试，覆盖 TCP/COM 双模式联调回归。
