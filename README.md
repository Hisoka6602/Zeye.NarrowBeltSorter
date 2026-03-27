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
│       ├── LeiMa/
│       │   ├── LeiMaLoopTrackManager.cs
│       │   ├── LeiMaModbusClientAdapter.cs
│       │   └── doc/
│       │       ├── 2-LM1000H 说明书.pdf
│       │       ├── (雷码)快速调机参数20250826.xlsx
│       │       ├── 雷码LM1000H说明书参数与调用逻辑梳理.md
│       │       └── 雷码快速调机参数变频器配置表梳理.md
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
  - `Events/Carrier/*.cs`：小车领域事件载荷定义（连接、载货、转向、速度、建环、感应位变更、故障隔离等）。
  - `Events/Chutes/*.cs`：格口领域事件载荷定义（状态、IO、补偿、落格、强排、连接、故障隔离等）。
  - `Manager/Carrier/ICarrier.cs`：单小车契约，定义状态只读属性、事件与连接/控制/装卸货异步方法。
  - `Manager/Carrier/ICarrierManager.cs`：小车管理器契约，定义建环、感应位、落格模式、故障隔离事件与管理方法。
  - `Manager/Chutes/IChute.cs`：单格口契约，定义状态、补偿、时窗、落格事件与配置写入方法。
  - `Manager/Chutes/IChuteManager.cs`：格口管理器契约，定义强排、目标口、锁格、配置快照、连接状态与管理方法。
  - `Manager/Chutes/IZhiQianModbusClientAdapter.cs`：智嵌 Modbus 客户端抽象接口，定义线圈读写最小能力（ConnectAsync、ReadDoStatesAsync、WriteSingleDoAsync、WriteBatchDoAsync）。
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
  - `ZhiQianChuteManagerTests.cs`：覆盖智嵌格口管理器配置合法性、连接流转、强排/锁格/目标更新、IO 状态同步与异常隔离行为（共 15 个测试用例）。
- `Zeye.NarrowBeltSorter.Drivers`：设备驱动与厂商资料。
  - `Vendors/LeiMa/LeiMaLoopTrackManager.cs`：`ILoopTrackManager` 的雷码 LM1000H 实现（连接、启停、设速、告警清除、轮询与事件发布），设速主链路固定写入 `P3.10(030AH)`，关键执行路径按 `slaveClients` 覆盖全部配置从站。
  - `Vendors/LeiMa/LeiMaModbusClientAdapter.cs`：雷码 Modbus 双模式适配器实现（TcpGateway/SerialRtu，统一 TouchSocket + TouchSocket.Modbus + Polly 重试）。
  - `Vendors/LeiMa/doc/2-LM1000H 说明书.pdf`：雷码 LM1000H 原始说明书。
  - `Vendors/LeiMa/doc/(雷码)快速调机参数20250826.xlsx`：雷码快速调机参数原始表。
  - `Vendors/LeiMa/doc/雷码LM1000H说明书参数与调用逻辑梳理.md`：从说明书与联调项目提取的参数与调用逻辑梳理（含出处）。
  - `Vendors/LeiMa/doc/雷码快速调机参数变频器配置表梳理.md`：从调机参数表提取的变频器配置参数梳理。
  - `Vendors/ZhiQian/ZhiQianChute.cs`：`IChute` 的智嵌格口实现（纯内存状态机，IoState 由 ZhiQianChuteManager 在 DO 写入/轮询后同步）。
  - `Vendors/ZhiQian/ZhiQianChuteManager.cs`：`IChuteManager` 的智嵌 32 路继电器实现，负责连接、轮询、强排/锁格/目标管理与 DO 写入，危险路径统一经 SafeExecutor 隔离。
  - `Vendors/ZhiQian/ZhiQianModbusClientAdapter.cs`：智嵌 Modbus 客户端适配器实现（ModbusTcp/ModbusRtu 双模式，统一 TouchSocket.Modbus + Polly 重试）。
  - `Vendors/ZhiQian/doc/【智嵌物联】32路网络继电器控制器用户使用手册V1.2.pdf`：智嵌厂商官方发布的 32 路网络继电器控制器原始用户手册（V1.2）。
  - `Vendors/ZhiQian/doc/智嵌32路网络继电器手册解析与IChuteManager接入方案.md`：基于智嵌手册整理的连接、IO 控制、透传、配置、测试与 `IChuteManager` 接入分析文档（含章节出处）。
- `Zeye.NarrowBeltSorter.Execution`：执行层（流程/调度相关）。
- `Zeye.NarrowBeltSorter.Host`：宿主程序与后台服务。
  - `Services/LogCleanupService.cs`：日志清理后台服务。
  - `Services/LoopTrackManagerService.cs`：LoopTrack 主运行服务，负责配置校验、连接重试、自动启动设速、闭环稳速监测、实时速度日志与 PID 调参日志，危险路径统一经 SafeExecutor 隔离。
  - `Services/LoopTrackHILWorker.cs`：上机联调后台服务，支持自动连接/清报警/设初始目标/自动启动、键盘停轨降级与全量关键事件结构化日志。
  - `Program.cs`：Host 入口与 DI 注册；按配置在 Main 模式与 HIL 模式二选一启用环轨后台服务；按 `Chutes:Enabled` 决定是否注册 ZhiQian 格口管理器。
  - `NLog.config`：NLog 分类日志路由配置，定义 status/pid/modbus/fault 分类目标与 chute-status/chute-modbus/chute-fault 格口分类目标，按各自开关独立控制落盘。
  - `appsettings*.json`：Host 配置文件，所有字段均附中文注释，新增 `Chutes:ZhiQian` 与 `Chutes:ZhiQian:Logging` 配置节，覆盖格口驱动连接、映射与三路分类日志参数。
- `Zeye.NarrowBeltSorter.Infrastructure`：基础设施层。
- `Zeye.NarrowBeltSorter.Ingress`：入口与接入层。
- `Zeye.NarrowBeltSorter.sln`：解决方案文件。

## 本次更新内容

- 新增智嵌 32 路网络继电器 `IChuteManager` 完整驱动实现，覆盖以下文件：
  - `Core/Enums/Chutes/ZhiQianTransport.cs`：传输模式枚举（ModbusTcp/ModbusRtu）。
  - `Core/Options/Chutes/ZhiQianChuteOptions.cs`：驱动配置对象，含内置合法性校验；嵌套 `Logging` 属性指向日志配置。
  - `Core/Options/Chutes/ZhiQianLoggingOptions.cs`：格口日志配置对象（EnableCategoryFile / CategoryLogDirectory / CategoryRetentionDays）。
  - `Core/Utilities/Chutes/ZhiQianAddressMap.cs`：静态地址映射工具，统一 Y 路编号与 Modbus 线圈地址换算。
  - `Core/Manager/Chutes/IZhiQianModbusClientAdapter.cs`：Modbus 通信最小能力接口。
  - `Drivers/Vendors/ZhiQian/ZhiQianModbusClientAdapter.cs`：TouchSocket.Modbus + Polly 双模式适配器实现。
  - `Drivers/Vendors/ZhiQian/ZhiQianChute.cs`：纯内存 IChute 实现，IoState 由管理器轮询同步。
  - `Drivers/Vendors/ZhiQian/ZhiQianChuteManager.cs`：完整 IChuteManager 实现，含连接、轮询、强排/锁格/目标更新与 SafeExecutor 隔离。
  - `Core.Tests/FakeZhiQianModbusClientAdapter.cs`：测试桩。
  - `Core.Tests/ZhiQianChuteManagerTests.cs`：15 个覆盖核心行为的单元测试。
- 更新 `Host/Program.cs`：按 `Chutes:Enabled` 注册智嵌格口管理器。
- 更新 `Host/NLog.config`：新增 `chuteLogDir` 变量与 chute-status / chute-modbus / chute-fault 三路 File 目标及路由规则，格口日志按 `Chutes:ZhiQian:Logging:EnableCategoryFile` 开关独立控制。
- 更新 `appsettings.json` / `appsettings.Development.json`：新增 `Chutes:ZhiQian` 与 `Chutes:ZhiQian:Logging` 配置节，所有字段附中文注释。
- 更新 README 文件树与职责说明。
