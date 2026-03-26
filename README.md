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
│   │   │   ├── LoopTrackHilOptions.cs
│   │   │   ├── LoopTrackLeiMaConnectionOptions.cs
│   │   │   ├── LoopTrackLeiMaSerialRtuOptions.cs
│   │   │   ├── LoopTrackLoggingOptions.cs
│   │   │   └── LoopTrackServiceOptions.cs
│   │   ├── Pid/
│   │   │   └── PidControllerOptions.cs
│   │   └── TrackSegment/
│   │       ├── LoopTrackConnectionOptions.cs
│   │       └── LoopTrackPidOptions.cs
│   └── Utilities/
│       ├── OperationIdFactory.cs
│       └── LoopTrack/
│           ├── LeiMaRegisters.cs
│           ├── LeiMaSpeedConverter.cs
│           ├── LoopTrackConsoleHelper.cs
│           └── LoopTrackLeiMaTransportModes.cs
├── Zeye.NarrowBeltSorter.Core.Tests/
│   ├── FakeLoopTrackManager.cs
│   ├── LeiMaLoopTrackManagerTests.cs
│   ├── LeiMaModbusClientAdapterTests.cs
│   ├── LoopTrackHILWorkerTests.cs
│   ├── LoopTrackManagerServiceTests.cs
│   ├── TestableLoopTrackHILWorker.cs
│   ├── TestableLoopTrackManagerService.cs
│   └── PidControllerTests.cs
├── Zeye.NarrowBeltSorter.Drivers/
│   └── Vendors/
│       └── LeiMa/
│           ├── LeiMaLoopTrackManager.cs
│           ├── LeiMaModbusClientAdapter.cs
│           └── doc/
│               ├── 2-LM1000H 说明书.pdf
│               ├── (雷码)快速调机参数20250826.xlsx
│               ├── 雷码LM1000H说明书参数与调用逻辑梳理.md
│               └── 雷码快速调机参数变频器配置表梳理.md
├── Zeye.NarrowBeltSorter.Execution/
├── Zeye.NarrowBeltSorter.Host/
│   ├── Services/
│   │   ├── LogCleanupService.cs
│   │   ├── LoopTrackHILWorker.cs
│   │   └── LoopTrackManagerService.cs
│   ├── Program.cs
│   ├── NLog.config
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
  - `Algorithms/PidControllerInput.cs`：PID 输入载荷，定义目标速度、实际速度与积分冻结标志。
  - `Algorithms/PidControllerState.cs`：PID 迭代状态，保存积分、上一帧误差与微分状态。
  - `Algorithms/PidControllerOutput.cs`：PID 输出载荷，包含命令频率、各项贡献与下一状态。
  - `Algorithms/PidController.cs`：PID 纯计算器实现，执行 mm/s→Hz 域计算、限幅与条件积分 anti-windup。
  - `Options/Pid/PidControllerOptions.cs`：PID 参数对象，包含系数、采样周期、限幅与滤波参数及合法性校验；`Validate` 支持注入 `ILogger`，在异常分支先记录日志再抛出异常。
  - `Options/LoopTrack/LoopTrackHilOptions.cs`：上机联调（HIL）配置定义，包含自动连接、自动启动、状态日志与键盘停轨参数。
  - `Options/TrackSegment/LoopTrackConnectionOptions.cs`：环形轨道连接参数定义（从站地址、超时、重试）。
  - `Options/TrackSegment/LoopTrackPidOptions.cs`：环形轨道 PID 参数定义（Kp/Ki/Kd）。
  - `Utilities/OperationIdFactory.cs`：统一短格式操作编号生成工具，供 Host/Drivers 复用以避免重复实现。
  - `Utilities/LoopTrack/LoopTrackConsoleHelper.cs`：环轨控制台交互环境检测工具，统一非交互环境降级判定逻辑。
- `Zeye.NarrowBeltSorter.Core.Tests`：核心单元测试项目。
  - `FakeLoopTrackManager.cs`：`ILoopTrackManager` 测试桩，覆盖连接、启停、断连与释放调用计数，支撑服务补偿链路断言。
  - `LoopTrackHILWorkerTests.cs`：覆盖上机联调 Worker 开关控制、自动连接/设速/启动、异常隔离与非法配置安全退出。
  - `PidControllerTests.cs`：覆盖参数校验、首帧微分、输出限幅、anti-windup 与冻结积分行为。
  - `LeiMaLoopTrackManagerTests.cs`：覆盖 LeiMa 环轨管理器连接流转、速度写入换算、启停复位命令与异常隔离行为。
  - `LeiMaModbusClientAdapterTests.cs`：覆盖 LeiMa Modbus 适配器构造参数边界校验。
  - `LoopTrackManagerServiceTests.cs`：覆盖 Transport 分支（TcpGateway/SerialRtu）、SerialRtu 非法参数安全退出与 AutoStart 失败补偿链路。
  - `TestableLoopTrackHILWorker.cs`：HIL Worker 测试专用派生类型，暴露执行入口并支持注入事件异常场景。
  - `TestableLoopTrackManagerService.cs`：服务测试专用派生类型，暴露受保护入口并统计管理器创建次数。
- `Zeye.NarrowBeltSorter.Drivers`：设备驱动与厂商资料。
  - `Vendors/LeiMa/LeiMaLoopTrackManager.cs`：`ILoopTrackManager` 的雷码 LM1000H 实现（连接、启停、设速、告警清除、轮询与事件发布），设速主链路固定写入 `P3.10(030AH)`，关键执行路径按 `slaveClients` 覆盖全部配置从站。
  - `Vendors/LeiMa/LeiMaModbusClientAdapter.cs`：雷码 Modbus 双模式适配器实现（TcpGateway/SerialRtu，统一 TouchSocket + TouchSocket.Modbus + Polly 重试）。
  - `Vendors/LeiMa/doc/2-LM1000H 说明书.pdf`：雷码 LM1000H 原始说明书。
  - `Vendors/LeiMa/doc/(雷码)快速调机参数20250826.xlsx`：雷码快速调机参数原始表。
  - `Vendors/LeiMa/doc/雷码LM1000H说明书参数与调用逻辑梳理.md`：从说明书与联调项目提取的参数与调用逻辑梳理（含出处）。
  - `Vendors/LeiMa/doc/雷码快速调机参数变频器配置表梳理.md`：从调机参数表提取的变频器配置参数梳理。
- `Zeye.NarrowBeltSorter.Execution`：执行层（流程/调度相关）。
- `Zeye.NarrowBeltSorter.Host`：宿主程序与后台服务。
  - `Services/LogCleanupService.cs`：日志清理后台服务。
  - `Services/LoopTrackManagerService.cs`：LoopTrack 主运行服务，负责配置校验、连接重试、自动启动设速、闭环稳速监测、实时速度日志与 PID 调参日志，危险路径统一经 SafeExecutor 隔离。
  - `Services/LoopTrackHILWorker.cs`：上机联调后台服务，支持自动连接/清报警/设初始目标/自动启动、键盘停轨降级与全量关键事件结构化日志。
  - `Program.cs`：Host 入口与 DI 注册；按配置在 Main 模式与 HIL 模式二选一启用环轨后台服务，避免同设备并发抢占。
  - `NLog.config`：NLog 分类日志路由配置，定义 status/pid/modbus/fault 分类目标、默认级别与过滤规则。
  - `appsettings*.json`：Host 配置文件，所有字段均附中文注释，覆盖 Logging、LogCleanup、LoopTrack（含 PID、ConnectRetry、Logging 与 Hil 子配置）。
- `Zeye.NarrowBeltSorter.Infrastructure`：基础设施层。
- `Zeye.NarrowBeltSorter.Ingress`：入口与接入层。
- `Zeye.NarrowBeltSorter.sln`：解决方案文件。

## 本次更新内容

- LoopTrack 配置改为仅支持 `SlaveAddresses`（移除 `SlaveAddress`），支持单元素数组表示单从站；新增 `SpeedAggregateStrategy`（`Min/Avg/Median`，默认 `Min`）。
- LeiMa 多从站能力增强：轮询按从站采样并按策略聚合速度；启停/设速/PID 写入改为广播到所有从站，任一关键写失败返回 `false` 并记录失败从站。
- 多从站关键路径补齐：连接状态判定改为“全部从站已连接”，清报警改为“全从站广播复位 + 全从站逐一回读故障码”，确保配置从站全部参与执行。
- `PidControllerOptions.Validate` 补齐 `ILogger` 日志闭环：参数越界时先记录结构化错误日志，再抛出 `ArgumentOutOfRangeException`。
- 事件载荷增强：`LoopTrackSpeedSamplingPartiallyFailedEventArgs` 新增失败从站编号字段；`LoopTrackSpeedSpreadTooLargeEventArgs` 采样明细改为每个从站速度样本。
- PID 默认值调整为稳健起步参数：`Kp=0.28`、`Ki=0.028`、`Kd=0.005`，并同步到 `appsettings.json` 与 `appsettings.Development.json`。
- 日志诊断增强：启动配置快照、连接/重试/采样失败统一 `operationId`，并增加“检查从站地址冲突/串口占用/终端电阻”建议动作。
- 日志与调试规划：驱动层 `LeiMaLoopTrackManager`、`LeiMaModbusClientAdapter` 使用 NLog 输出诊断日志；Host 层通过 `Microsoft.Extensions.Logging` 并额外接入 NLog provider，实现分类日志路由且保留默认 provider 输出。
- 单元测试新增/更新：覆盖多从站配置解析（含单元素）、`Min/Avg/Median` 聚合、部分失败继续聚合、全失败行为、写入广播失败链路。

## 后续可完善点

- 将多从站采样由当前串行读取升级为并行读取，并增加限流与超时预算，进一步降低轮询抖动。
- 为 LoopTrack 独立调试日志增加可配置保留天数与级别阈值映射，提升现场可运维性。
