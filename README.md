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
│   │   ├── Carrier/
│   │   ├── Chutes/
│   │   │   ├── ChuteStatus.cs
│   │   │   ├── ParcelToChuteDistanceLevel.cs
│   │   │   ├── WriteVerifyMode.cs
│   │   │   └── ZhiQianTransport.cs
│   │   ├── Device/
│   │   ├── Io/
│   │   ├── Parcel/
│   │   ├── Realtime/
│   │   ├── Sorting/
│   │   ├── System/
│   │   └── Track/
│   ├── Algorithms/
│   │   ├── PidController设计规划.md
│   │   ├── PidControllerInput.cs
│   │   ├── PidControllerState.cs
│   │   ├── PidControllerOutput.cs
│   │   └── PidController.cs
│   ├── Events/
│   │   ├── Carrier/
│   │   ├── Chutes/
│   │   ├── Io/
│   │   ├── Parcel/
│   │   ├── Realtime/
│   │   └── Track/
│   ├── Manager/
│   │   ├── Carrier/
│   │   │   ├── ICarrier.cs
│   │   │   └── ICarrierManager.cs
│   │   ├── Chutes/
│   │   │   ├── IChute.cs
│   │   │   ├── IChuteManager.cs
│   │   │   └── IZhiQianModbusClientAdapter.cs
│   │   └── TrackSegment/
│   │       ├── ILoopTrackManager.cs
│   │       └── ILeiMaModbusClientAdapter.cs
│   ├── Models/
│   ├── Options/
│   │   ├── Chutes/
│   │   │   ├── ChuteForcedRotationOptions.cs
│   │   │   ├── ZhiQianChuteOptions.cs
│   │   │   └── ZhiQianLoggingOptions.cs
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
│       ├── Chutes/
│       │   └── ZhiQianAddressMap.cs
│       └── LoopTrack/
│           ├── LeiMaRegisters.cs
│           ├── LeiMaSpeedConverter.cs
│           ├── LoopTrackConsoleHelper.cs
│           └── LoopTrackLeiMaTransportModes.cs
├── Zeye.NarrowBeltSorter.Core.Tests/
│   ├── FakeLoopTrackManager.cs
│   ├── FakeZhiQianModbusClientAdapter.cs
│   ├── LeiMaLoopTrackManagerTests.cs
│   ├── LeiMaModbusClientAdapterTests.cs
│   ├── LoopTrackHILWorkerTests.cs
│   ├── LoopTrackManagerServiceTests.cs
│   ├── TestableLoopTrackHILWorker.cs
│   ├── TestableLoopTrackManagerService.cs
│   ├── PidControllerTests.cs
│   └── ZhiQianChuteManagerTests.cs
├── Zeye.NarrowBeltSorter.Drivers/
│   └── Vendors/
│       ├── Leadshaine/
│       │   ├── Emc/
│       │   │   ├── LTDMC.cs
│       │   │   └── LTDMC.dll
│       │   └── doc/
│       │       ├── LeadshaineEmcController完整接入与IO监控步骤.md
│       │       └── LDC-FJ-RF红外驱动器接入ICarrierManager详细方案.md
│       ├── LeiMa/
│       │   ├── LeiMaLoopTrackManager.cs
│       │   ├── LeiMaModbusClientAdapter.cs
│       │   └── doc/
│       │       ├── 2-LM1000H 说明书.pdf
│       │       ├── (雷码)快速调机参数20250826.xlsx
│       │       ├── 雷码LM1000H说明书参数与调用逻辑梳理.md
│       │       ├── 雷码快速调机参数变频器配置表梳理.md
│       │       ├── 多从站稳速难题分析与工程解决方案.md
│       │       └── 雷码Modbus读写超时根因分析.md
│       └── ZhiQian/
│           ├── ZhiQianChute.cs
│           ├── ZhiQianChuteManager.cs
│           ├── ZhiQianModbusClientAdapter.cs
│           └── doc/
│               ├── 【智嵌物联】32路网络继电器控制器用户使用手册V1.2.pdf
│               └── 智嵌32路网络继电器手册解析与IChuteManager接入方案.md
├── Zeye.NarrowBeltSorter.Execution/
├── Zeye.NarrowBeltSorter.Host/
│   ├── Services/
│   │   ├── ChuteForcedRotationService.cs
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
  - `Enums/Device/DeviceConnectionStatus.cs`：设备通用连接状态枚举，供 Carrier/Chute 侧复用，避免轨道专用命名泄漏到非轨道域。
  - `Enums/Chutes/ZhiQianTransport.cs`：智嵌继电器通信传输模式枚举（ModbusTcp / ModbusRtu）。
  - `Enums/Chutes/WriteVerifyMode.cs`：智嵌 DO 写后读校验策略枚举（WarnOnly / RetryThenFail），用于控制写校验失败后的处理路径。
  - `Events/Carrier/*.cs`：小车领域事件载荷定义（连接、载货、转向、速度、建环、感应位变更、故障隔离等）。
  - `Events/Chutes/*.cs`：格口领域事件载荷定义（状态、IO、补偿、落格、强排、连接、故障隔离等）。
  - `Manager/Carrier/ICarrier.cs`：单小车契约，定义状态只读属性、事件与连接/控制/装卸货异步方法。
  - `Manager/Carrier/ICarrierManager.cs`：小车管理器契约，定义建环、感应位、落格模式、故障隔离事件与管理方法。
  - `Manager/Chutes/IChute.cs`：单格口契约，定义状态、补偿、时窗、落格事件与配置写入方法。
  - `Manager/Chutes/IChuteManager.cs`：格口管理器契约，定义强排、目标口、锁格、配置快照、连接状态与管理方法。
  - `Manager/Chutes/IZhiQianModbusClientAdapter.cs`：智嵌 Modbus 客户端抽象接口，定义线圈读写最小能力（ConnectAsync、ReadDoStatesAsync、WriteSingleDoAsync、WriteBatchDoAsync）。
  - `Options/Chutes/ChuteForcedRotationOptions.cs`：格口强排轮转后台服务配置对象，定义启用开关、切换周期与轮转数组。
  - `Options/Chutes/ZhiQianChuteOptions.cs`：智嵌 32 路继电器格口驱动配置对象，包含传输模式、连接参数、超时重试、格口绑定映射与内置合法性校验；嵌套 `Logging` 属性指向日志配置。
  - `Options/Chutes/ZhiQianLoggingOptions.cs`：智嵌格口日志配置对象，控制 chute-status / chute-modbus / chute-fault 三路分类日志的落盘目录、开关与保留天数。
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
  - `Utilities/Chutes/ZhiQianAddressMap.cs`：智嵌 32 路继电器静态地址映射工具，统一 Y 路编号（1~32）与 Modbus 线圈地址（0-based）之间的换算，避免分散实现。
  - `Utilities/LoopTrack/LoopTrackConsoleHelper.cs`：环轨控制台交互环境检测工具，统一非交互环境降级判定逻辑。
- `Zeye.NarrowBeltSorter.Core.Tests`：核心单元测试项目。
  - `FakeLoopTrackManager.cs`：`ILoopTrackManager` 测试桩，覆盖连接、启停、断连与释放调用计数，支撑服务补偿链路断言。
  - `FakeZhiQianModbusClientAdapter.cs`：`IZhiQianModbusClientAdapter` 测试桩，提供内存 DO 读写、连接/断开计数与异常注入能力，供单元测试使用。
  - `LoopTrackHILWorkerTests.cs`：覆盖上机联调 Worker 开关控制、自动连接/设速/启动、异常隔离与非法配置安全退出。
  - `PidControllerTests.cs`：覆盖参数校验、首帧微分、输出限幅、anti-windup 与冻结积分行为。
  - `LeiMaLoopTrackManagerTests.cs`：覆盖 LeiMa 环轨管理器连接流转、速度写入换算、启停复位命令与异常隔离行为。
  - `LeiMaModbusClientAdapterTests.cs`：覆盖 LeiMa Modbus 适配器构造参数边界校验。
  - `LoopTrackManagerServiceTests.cs`：覆盖 Transport 分支（TcpGateway/SerialRtu）、SerialRtu 非法参数安全退出与 AutoStart 失败补偿链路。
  - `TestableLoopTrackHILWorker.cs`：HIL Worker 测试专用派生类型，暴露执行入口并支持注入事件异常场景。
  - `TestableLoopTrackManagerService.cs`：服务测试专用派生类型，暴露受保护入口并统计管理器创建次数。
  - `ZhiQianChuteManagerTests.cs`：覆盖智嵌格口管理器配置合法性、连接门控、强排/锁格冲突防护、时窗开关闸、轮询自动重连、写后读策略与异常隔离行为。
- `Zeye.NarrowBeltSorter.Drivers`：设备驱动与厂商资料。
  - `Vendors/Leadshaine/Emc/LTDMC.cs`：雷赛 EMC SDK 的 C# P/Invoke 封装声明，提供底层函数签名映射。
  - `Vendors/Leadshaine/Emc/LTDMC.dll`：雷赛 EMC 运行时动态库，供驱动层调用底层 IO/控制能力。
  - `Vendors/Leadshaine/doc/LeadshaineEmcController完整接入与IO监控步骤.md`：基于 `WheelDiverterSorter` 的 `LeadshaineEmcController` 定义与使用分析，以及本仓库完整接入与 IO 监控落地步骤；厂商命名统一为 `Leadshaine`。
  - `Vendors/Leadshaine/doc/LDC-FJ-RF红外驱动器接入ICarrierManager详细方案.md`：面向 `ICarrierManager` 的 LDC-FJ-RF 接入蓝图，覆盖连接、控制、状态映射、事件触发、验收清单与手册到位后的逐条核对模板。
  - `Vendors/LeiMa/LeiMaLoopTrackManager.cs`：`ILoopTrackManager` 的雷码 LM1000H 实现（连接、启停、设速、告警清除、轮询与事件发布），设速主链路固定写入 `P3.10(030AH)`，关键执行路径按 `slaveClients` 覆盖全部配置从站。
  - `Vendors/LeiMa/LeiMaModbusClientAdapter.cs`：雷码 Modbus 双模式适配器实现（TcpGateway/SerialRtu，统一 TouchSocket + TouchSocket.Modbus + Polly 重试）。
  - `Vendors/LeiMa/doc/2-LM1000H 说明书.pdf`：雷码 LM1000H 原始说明书。
  - `Vendors/LeiMa/doc/(雷码)快速调机参数20250826.xlsx`：雷码快速调机参数原始表。
  - `Vendors/LeiMa/doc/雷码LM1000H说明书参数与调用逻辑梳理.md`：从说明书与联调项目提取的参数与调用逻辑梳理（含出处）。
  - `Vendors/LeiMa/doc/雷码快速调机参数变频器配置表梳理.md`：从调机参数表提取的变频器配置参数梳理。
  - `Vendors/LeiMa/doc/多从站稳速难题分析与工程解决方案.md`：系统分析多从站 Modbus 闭环稳速难以收敛的根本原因（采样时差、皮带耦合、统一转矩偏差、噪声放大等），并对比主从转矩跟随、电子轴、下垂控制、交叉耦合、MPC 等工业界主流解决方案与代表产品，给出当前架构的阶段性改进建议。
  - `Vendors/LeiMa/doc/雷码Modbus读写超时根因分析.md`：分析 Polly `TimeoutStrategy.Pessimistic` 导致幽灵帧级联超时的根因，及三层串行门控的设计正确性说明。
  - `Vendors/ZhiQian/ZhiQianChute.cs`：`IChute` 的智嵌格口实现（纯内存状态机，IoState 由 ZhiQianChuteManager 在 DO 写入/轮询后同步）。
  - `Vendors/ZhiQian/ZhiQianChuteManager.cs`：`IChuteManager` 的智嵌 32 路继电器实现，负责连接、轮询、自动重连、强排/锁格/目标管理、时窗开关闸与 DO 写入，危险路径统一经 SafeExecutor 隔离。
  - `Vendors/ZhiQian/ZhiQianModbusClientAdapter.cs`：智嵌 Modbus 客户端适配器实现（ModbusTcp/ModbusRtu 双模式，统一 TouchSocket.Modbus + Polly 重试）。
  - `Vendors/ZhiQian/doc/【智嵌物联】32路网络继电器控制器用户使用手册V1.2.pdf`：智嵌厂商官方发布的 32 路网络继电器控制器原始用户手册（V1.2）。
  - `Vendors/ZhiQian/doc/智嵌32路网络继电器手册解析与IChuteManager接入方案.md`：基于智嵌手册整理的连接、IO 控制、透传、配置、测试与 `IChuteManager` 接入分析文档（含章节出处）。
- `Zeye.NarrowBeltSorter.Execution`：执行层（流程/调度相关）。
- `Zeye.NarrowBeltSorter.Host`：宿主程序与后台服务。
  - `Services/ChuteForcedRotationService.cs`：格口强排轮转后台服务；连接成功后按配置数组每隔固定秒数切换强排口。
  - `Services/LogCleanupService.cs`：日志清理后台服务。
  - `Services/LoopTrackManagerService.cs`：LoopTrack 主运行服务，负责配置校验、连接重试、自动启动设速、闭环稳速监测、实时速度日志与 PID 调参日志，危险路径统一经 SafeExecutor 隔离。
  - `Services/LoopTrackHILWorker.cs`：上机联调后台服务，支持自动连接/清报警/设初始目标/自动启动、键盘停轨降级与全量关键事件结构化日志。
  - `Program.cs`：Host 入口与 DI 注册；按配置在 Main 模式与 HIL 模式二选一启用环轨后台服务；按 `Chutes:Enabled` 决定是否注册 ZhiQian 格口管理器，并可启用格口强排轮转后台服务。
  - `NLog.config`：NLog 分类日志路由配置，定义 status/pid/modbus/fault 分类目标与 chute-status/chute-modbus/chute-fault 格口分类目标，按各自开关独立控制落盘。
  - `appsettings*.json`：Host 配置文件，所有字段均附中文注释，新增 `Chutes:ZhiQian` 与 `Chutes:ZhiQian:Logging` 配置节，覆盖格口驱动连接、映射与三路分类日志参数。
- `Zeye.NarrowBeltSorter.Infrastructure`：基础设施层。
- `Zeye.NarrowBeltSorter.Ingress`：入口与接入层。
- `Zeye.NarrowBeltSorter.sln`：解决方案文件。

## 本次更新内容

- 新增多从站稳速难题技术分析文档 `Vendors/LeiMa/doc/多从站稳速难题分析与工程解决方案.md`：
  - 系统分析单从站易稳速、多从站难稳速的根本原因（顺序 Modbus 轮询时差、皮带机械耦合、统一转矩偏差、聚合偏差、噪声放大与 PID 稳定裕度压缩）。
  - 对比工业界主流解决方案：主从转矩跟随、电子轴/虚拟主轴、下垂控制、交叉耦合控制、MPC。
  - 列出西门子 SINAMICS S120、ABB ACS880、安川 GA700 等代表产品与案例。
  - 给出当前架构（Modbus RTU/TCP + 单 PID）的阶段性改进建议（短期调参、中期主从跟随、长期并行采样+分布式 PID）。
- 更新 README 文件树与职责说明。

## 后续可完善点

- 在获取 `LDC-FJ-RF分拣专用红外驱动器使用说明书-20240618.pdf` 后，逐条补全 LDC 文档中的命令字、帧格式、参数与页码出处。
- 按文档蓝图落地 `LdcCarrierManager` 与协议适配器实现，并补充对应单元测试与联调验证用例。
