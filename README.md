# Zeye.NarrowBeltSorter

## 项目文件树（核心）

```text
Zeye.NarrowBeltSorter.sln
├── .github/workflows/cleanup-copilot-codex-branches.yml # 自动清理名称含 copilot/codex 的远程分支工作流（手动+定时触发）
├── Manager接口结构清单.md                 # 按 Manager 目录分章节维护接口结构树状图
├── 设备代码结构清单.md                    # 按设备分章节维护设备代码结构树状图
├── 配置文件拆分分析.md                    # 配置文件按能力模块拆分的边界说明与加载顺序文档
├── IIoPanel定义与联动IO服务两阶段实施计划.md # 对标 WheelDiverterSorter 的 IIoPanel 与联动 IO 服务 2 PR 落地计划
├── WheelDiverterSorter_OnLineSetting_IO按钮状态流转分析.md # 分析 OnLine-Setting 分支中 IoPanel 按钮触发系统状态变更的完整链路
├── 西门子S7实施计划（三个拉取请求落地）.md  # 对标 WheelDiverterSorter 的 SiemensS7 实现并给出三阶段落地计划
├── LeadshaineEmcController实施计划（三个拉取请求落地）.md  # 对标 WheelDiverterSorter 的 LeadshaineEmcController 实现并给出三阶段落地计划
├── 红外参数生效与落格触发延迟分析.md       # 红外参数生效边界、CarrierId日志语义与落格触发延迟成因分析文档
├── Zeye.NarrowBeltSorter.Core
│   ├── Manager/Chutes
│   │   ├── IChuteManager.cs                # 格口管理器统一抽象
│   │   ├── IZhiQianClientAdapter.cs        # 智嵌协议无关客户端接口
│   │   └── IZhiQianClientAdapterFactory.cs # 智嵌客户端适配器工厂接口
│   ├── Manager/InductionLane
│   │   ├── IInductionLaneManager.cs        # 供包通道管理器抽象
│   │   └── IInductionLane.cs               # 单路供包台抽象（状态/事件/控制）
│   ├── Manager/SignalTower
│   │   └── ISignalTower.cs                 # 单个信号塔抽象（灯/蜂鸣器/连接）
│   ├── Manager/Emc
│   │   ├── IEmcController.cs               # EMC 控制器统一抽象（初始化/监控/写入/重连）
│   │   └── IEmcHardwareAdapter.cs          # EMC 硬件访问适配器抽象
│   ├── Manager/IoPanel
│   │   └── IIoPanel.cs                     # IoPanel 操作面板管理器抽象（按钮边沿检测与事件发布）
│   ├── Enums/InductionLane
│   │   └── InductionLaneStatus.cs          # 供包台状态枚举
│   ├── Enums/SignalTower
│   │   ├── SignalTowerLightStatus.cs       # 信号塔三色灯状态枚举
│   │   └── BuzzerStatus.cs                 # 信号塔蜂鸣器状态枚举
│   ├── Enums/Emc
│   │   └── EmcControllerStatus.cs          # EMC 状态枚举
│   ├── Enums/Io
│   │   ├── IoState.cs                       # IO 电平状态枚举
│   │   ├── IoPointType.cs                   # IO 点位类型枚举
│   │   ├── IoPanelButtonType.cs             # IoPanel 按钮角色枚举（启动/停止/急停/复位）
│   │   └── IoPanelMonitoringStatus.cs       # IoPanel 监控状态枚举（Stopped/Monitoring/Faulted）
│   ├── Options/InductionLane
│   │   └── InductionLaneOptions.cs         # 供包台配置模型
│   ├── Options/Emc/Leadshaine
│   │   ├── LeadshaineEmcConnectionOptions.cs # Leadshaine EMC 连接参数配置与边界校验
│   │   ├── LeadshaineIoLinkageOptions.cs # Leadshaine 联动 IO 配置集合（启用开关与规则列表）
│   │   ├── LeadshaineIoLinkagePointOptions.cs # Leadshaine 联动 IO 单点规则配置（状态、点位、延迟、时长）
│   │   ├── LeadshaineIoPointBindingCollectionOptions.cs  # Leadshaine 点位绑定集合配置
│   │   ├── LeadshaineIoPointBindingOption.cs # Leadshaine 单点位逻辑绑定定义
│   │   └── LeadshaineBitBindingOption.cs     # Leadshaine 物理位绑定定义
│   ├── Events/InductionLane
│   │   ├── InductionLaneParcelCreatedEventArgs.cs # 供包台包裹创建事件载荷
│   │   ├── InductionLaneParcelArrivedAtLoadingPositionEventArgs.cs # 包裹到达上车位事件载荷
│   │   └── InductionLaneStatusChangedEventArgs.cs # 供包台状态变化事件载荷
│   ├── Events/SignalTower
│   │   ├── SignalTowerLightStatusChangedEventArgs.cs # 三色灯状态变化事件载荷
│   │   ├── SignalTowerBuzzerStatusChangedEventArgs.cs # 蜂鸣器状态变化事件载荷
│   │   └── SignalTowerConnectionStatusChangedEventArgs.cs # 连接状态变化事件载荷
│   ├── Events/Emc
│   │   ├── EmcInitializedEventArgs.cs      # EMC 初始化完成事件载荷
│   │   ├── EmcStatusChangedEventArgs.cs    # EMC 状态变化事件载荷
│   │   └── EmcFaultedEventArgs.cs          # EMC 故障事件载荷
│   ├── Events/IoPanel
│   │   ├── IoPanelButtonPressedEventArgs.cs       # IoPanel 按钮按下事件载荷（电平到达 TriggerState）
│   │   ├── IoPanelButtonReleasedEventArgs.cs      # IoPanel 急停按钮释放事件载荷（电平离开 TriggerState）
│   │   ├── IoPanelMonitoringStatusChangedEventArgs.cs  # IoPanel 监控状态变更事件载荷
│   │   └── IoPanelFaultedEventArgs.cs      # IoPanel 异常事件载荷
│   ├── Options/Chutes
│   │   ├── ZhiQianChuteOptions.cs          # 智嵌共享配置（含 Devices 列表）
│   │   ├── ZhiQianDeviceOptions.cs         # 单设备配置与逐台校验
│   │   └── ZhiQianLoggingOptions.cs        # 格口日志配置
│   ├── Utilities/Chutes/ZhiQianAddressMap.cs # DO 通道边界与索引校验
│   ├── Utilities/PointBindingReferenceValidator.cs # 点位引用绑定通用校验工具（跨厂商复用）
│   ├── Utilities/SensorWorkflowHelper.cs # 传感器监控工作流通用辅助（点位同步/去抖判定）
│   └── Utilities/IoBindingHelper.cs      # IO 绑定配置通用解析工具（TriggerState 解析，跨厂商复用）
├── Zeye.NarrowBeltSorter.Drivers
│   └── Vendors
│       ├── LeiMa/
│       │   ├── LeiMaModbusClientAdapter.cs            # 雷码 Modbus 客户端适配器（TCP/RTU + 重试超时 + 共享串口连接）
│       │   ├── LeiMaSerialRtuSharedConnection.cs      # 雷码串口 RTU 共享连接上下文（连接键/门控/引用计数）
│       │   └── doc/
│       │       └── 多从站稳速难题分析与工程解决方案.md  # 多从站闭环稳速根因拆解与工程解法对比
│       ├── Leadshaine/
│       │   ├── Infrared/
│       │   │   └── LeadshaineInfraredDriverFrameCodec.cs # LDC-FJ-RF 红外 8 字节帧编解码（D1~D4/99H）
│       │   └── Validators/
│       │       ├── LeadshainePointBindingOptionsValidator.cs # PointId 唯一与地址合法性校验
│       │       ├── LeadshaineIoPanelButtonOptionsBindingValidator.cs # IoPanel 引用点位校验
│       │       └── LeadshaineSensorOptionsBindingValidator.cs # Sensor 引用点位校验
│       │   └── Emc/
│       │       ├── LTDMC.cs # 雷赛运动控制底层互操作封装（厂商 SDK P/Invoke 绑定，规则豁免）
│       │       ├── LeadshaineEmcController.cs # Leadshaine EMC 控制器实现
│       │       ├── LeadshaineEmcHardwareAdapter.cs # Leadshaine EMC 硬件访问适配器实现
│       │       ├── LeadshaineIoPanel.cs # Leadshaine IoPanel 实现（消费 EMC 快照，按钮边沿检测并发布事件）
│       │       └── Options/
│       │           ├── LeadshainePointBindingCollectionOptions.cs # Leadshaine 点位绑定集合（Drivers）
│       │           ├── LeadshainePointBindingOptions.cs # Leadshaine 单点位绑定（Drivers）
│       │           ├── LeadshaineBitBindingOptions.cs # Leadshaine 物理位绑定（Drivers）
│       │           ├── LeadshainePointReferenceOptions.cs # Leadshaine 点位引用基础配置
│       │           ├── LeadshaineIoPanelButtonBindingCollectionOptions.cs # IoPanel 按钮绑定集合
│       │           ├── LeadshaineIoPanelButtonBindingOptions.cs # IoPanel 按钮绑定定义
│       │           ├── LeadshaineSensorBindingCollectionOptions.cs # Sensor 绑定集合
│       │           └── LeadshaineSensorBindingOptions.cs # Sensor 绑定定义
│       │   ├── Sensor/
│       │   │   └── LeadshaineSensorManager.cs # Leadshaine 传感器管理器（消费 EMC 快照）
│       └── ZhiQian
│           ├── ZhiQianBinaryClientAdapter.cs   # 二进制写 + ASCII读，串行门控/重连重试
│           ├── ZhiQianChuteManager.cs          # 单设备格口管理器
│           └── ZhiQianClientAdapterFactory.cs  # 默认工厂实现
├── Zeye.NarrowBeltSorter.Execution
│   ├── Parcel
│   │   ├── ParcelManager.cs # 包裹生命周期管理器（分片锁 + 事件发布）
│   │   ├── ParcelInfoReadOnlyView.cs # ConcurrentDictionary 只读视图，零拷贝枚举
│   │   └── ParcelManagerLog.cs # 包裹管理器高性能结构化日志定义（源生成）
│   └── Services
│       ├── ChuteSelfHandlingHostedService.cs # 格口自处理托管编排服务
│       ├── ChuteForcedRotationHostedService.cs # 格口强排托管编排服务（轮转/固定双模式互斥）
│       ├── SortingTaskOrchestrationService.cs # 分拣主协调托管服务（包裹创建与成熟泵送、事件编排）
│       ├── SortingTaskCarrierLoadingService.cs # 分拣上车编排服务（成熟队列消费、上车绑定、Carrier-Parcel映射）
│       ├── SortingTaskDropOrchestrationService.cs # 分拣落格编排服务（到位映射、落格执行、解绑回收）
│       ├── LoopTrackManagerHostedService.cs # 环轨托管编排服务
│       ├── LoopTrackHILHostedService.cs # 环轨 HIL 托管编排服务
│       ├── LogCleanupHostedService.cs # 日志清理托管编排服务
│       ├── State/LocalSystemStateManager.cs # 本地系统状态管理器实现
│       └── Hosted
│           ├── IoMonitoringHostedService.cs # Leadshaine Io 监控托管编排服务
│           ├── IoPanelStateTransitionHostedService.cs # IoPanel 按钮到系统状态桥接托管服务（Start/Stop/急停/复位）
│           └── IoLinkageHostedService.cs # Leadshaine 联动 Io 托管服务（系统状态到输出点位）
│   └── Properties
│       └── AssemblyInfo.cs # 声明 InternalsVisibleTo 给测试项目访问 Execution 层 internal API
├── Zeye.NarrowBeltSorter.Host
│   ├── Program.cs                          # 宿主启动入口（精简为步骤式顶层代码，调用各扩展方法注册）
│   ├── Vendors/DependencyInjection/
│   │   ├── HostApplicationBuilderConfigurationExtensions.cs  # 配置源加载扩展（按能力拆分的多层 JSON 文件）
│   │   ├── HostApplicationBuilderLeadshaineExtensions.cs     # Leadshaine EMC 厂商注册（含 IoMonitoring/IoPanel/IoLinkage/环组）
│   │   ├── HostApplicationBuilderZhiQianExtensions.cs        # 智嵌格口厂商注册（含强排/落格模拟）
│   │   ├── HostApplicationBuilderSortingExtensions.cs        # 分拣任务编排托管服务注册
│   │   ├── HostApplicationBuilderLoopTrackExtensions.cs      # 环轨托管服务注册（正式/HIL 两种模式）
│   │   └── LeadshaineOptionsDelegateValidator.cs             # Leadshaine 启动校验委托适配器
│   ├── appsettings.json                    # 全局基础默认配置（模板值，不含环境专属参数）
│   ├── appsettings.devices.json            # 全局设备硬件参数（串口/IP/映射，生产默认）
│   ├── appsettings.Development.json        # Development 通用覆盖（LogCleanup + Logging 级别）
│   ├── appsettings.Development.looptrack.json  # Development 环轨覆盖（LoopTrack 模块）
│   ├── appsettings.Development.chutes.json     # Development 格口覆盖（Chutes + Carrier 模块）
│   ├── appsettings.Development.leadshaine.json # Development Leadshaine 覆盖（EMC/IoPanel/Sensor/SignalTower）
│   └── appsettings.Development.devices.json    # Development 设备硬件参数覆盖（串口/IP/点位映射）
└── Zeye.NarrowBeltSorter.Core.Tests
    ├── FakeZhiQianClientAdapter.cs         # 智嵌客户端测试桩
    ├── ZhiQianChuteManagerTests.cs         # 格口管理器行为测试
    ├── Leadshaine/
    │   ├── LeadshaineEmcConnectionOptionsTests.cs # EMC 连接参数边界校验测试
    │   ├── Emc/
    │   │   ├── FakeLeadshaineEmcHardwareAdapter.cs # Leadshaine EMC 硬件访问测试桩
    │   │   ├── LeadshaineEmcControllerTestFactory.cs # Leadshaine EMC 控制器测试工厂
    │   │   ├── LeadshaineEmcControllerInitializationTests.cs # EMC 初始化状态流转测试
    │   │   ├── LeadshaineEmcControllerWriteIoTests.cs # EMC 输出写入边界测试
    │   │   ├── LeadshaineEmcControllerReconnectTests.cs # EMC 重连恢复测试
    │   │   └── LeadshaineEmcControllerMonitoringTests.cs # EMC 监控循环、断链检测与 TryGetMonitoredPoint 测试
    │   └── Integration/
    │       ├── FakeLeadshaineEmcController.cs # Leadshaine 集成测试用 EMC 控制器桩
    │       ├── FakeSystemStateManager.cs # 系统状态管理器测试桩
    │       ├── LeadshaineIoMonitoringHostedServiceTests.cs # IoMonitoringHostedService 编排链路测试
    │       ├── IoPanelStateTransitionHostedServiceTests.cs # IoPanel 按钮触发系统状态变更桥接测试
    │       ├── IoButtonLinkageEndToEndTests.cs # IO 按钮到 IoLinkage 输出写入端到端链路测试
    │       ├── LeadshaineIoLinkageHostedServiceTests.cs # IoLinkageHostedService 状态联动测试
    │       └── LeadshaineSensorManagerDebounceTests.cs # Leadshaine 传感器去抖与点位同步测试
```

## 各关键文件实现说明

- `IZhiQianClientAdapter.cs`：抽象连接、读 32 路状态、单写、批写能力，解耦具体协议实现。
- `IInductionLane.cs`：按注释补全供包台契约（连接/状态/IO/包裹事件/配置/启停方法）。
- `ISignalTower.cs`：按注释补全信号塔契约（三色灯/蜂鸣器/连接状态、状态事件、控制方法）。
- `InductionLaneStatus.cs`：供包台状态枚举。
- `SignalTowerLightStatus.cs` 与 `BuzzerStatus.cs`：信号塔三色灯与蜂鸣器状态枚举。
- `InductionLaneOptions.cs`：供包台配置对象（距离、速度、IO、包裹长度监控等）。
- `Events/InductionLane/*.cs`：供包台包裹创建、到达上车位、状态变化事件载荷。
- `Events/SignalTower/*.cs`：信号塔灯态、蜂鸣器、连接状态变化事件载荷。
- `ZhiQianDeviceOptions.cs`：定义单台智嵌设备 `Host/Port/DeviceAddress/ChuteToDoMap`，并提供 `Validate(deviceIndex)`。
- `ZhiQianChuteOptions.cs`：定义共享参数与 `Devices` 列表；当前限制 1 台设备，同时提供旧版顶层 `Host/Port/DeviceAddress/ChuteToDoMap` 的兼容映射（自动归一化到 `Devices[0]`）。
- `ZhiQianAddressMap.cs`：仅保留 DO 边界常量与 `ValidateDoIndex`，移除 Modbus 线圈换算。
- `ZhiQianBinaryClientAdapter.cs`：
  - `_requestGate` 串行化单连接内所有命令；
  - 单写使用 `0x70` 固定 10 字节帧，不等应答；
  - 批写先回读再合并，再发 `0x57` 15 字节帧（含 checksum），保证原子序列；
  - 读用 ASCII 命令 `zq {addr} get y qz`，TouchSocket `AddTcpReceivedPlugin` 将收到的数据追加到 `StringBuilder`，优先按 `qz` 帧尾切片，若设备返回未带 `qz` 的换行文本则按 `\n` 兜底切片，再写入 `Channel<string>`；
  - 读超时重试时先重连并清空缓冲，避免幽灵应答污染。
- `ZhiQianChuteManager.cs`：负责连接状态、轮询回读、写后读校验、自动重连与故障事件发布。
- `FakeZhiQianClientAdapter.cs`：提供内存态 DO 读写测试桩，支持连接失败/写失败/读失败与写后读不一致场景模拟。
- `LeadshaineInfraredDriverFrameCodec.cs`：实现 `IInfraredDriverFrameCodec`，按手册规则编码 D1~D4 8 字节帧，并解析 99H 回包（Byte2~4 异或校验 + 故障位提取）。
- `Options/Emc/Leadshaine/*.cs`（Core）：按“能力优先、厂商次级”定义 Leadshaine EMC 连接参数、点位集合与位绑定模型，并提供基础边界校验。
- `Vendors/Leadshaine/Emc/Options/*.cs`（Drivers）：定义 Leadshaine 的点位集合、按钮/传感器绑定集合与物理位绑定模型（统一归属 EMC 子级）。
- `Vendors/Leadshaine/Validators/*.cs`（Drivers）：提供 PointId 唯一性、区域/位索引合法性、IoPanel/Sensor 引用关系校验。
- `PointBindingReferenceValidator.cs`（Core.Utilities）：点位引用绑定通用校验工具，支持泛型配置类型，跨厂商复用避免重复实现。
- `HostApplicationBuilderLeadshaineExtensions.cs`：统一注册 Leadshaine 配置绑定与 ValidateOnStart 启动前校验。
- `LeadshaineOptionsDelegateValidator.cs`：将配置校验委托统一适配为 `IValidateOptions<T>`，输出完整错误集合。
- `IEmcController.cs`：定义 EMC 初始化、重连、点位监控注册、单点位查询（`TryGetMonitoredPoint`）与写入抽象能力。
- `IEmcHardwareAdapter.cs`：定义 EMC 底层硬件调用抽象，隔离 LTDMC 互操作实现细节。
- `Events/Emc/*.cs`：定义 EMC 初始化、状态变化、故障三类事件载荷。
- `IIoPanel.cs`：定义 IoPanel 操作面板管理器抽象（按角色分发事件：StartButtonPressed/StopButtonPressed/EmergencyStopButtonPressed/ResetButtonPressed/EmergencyStopButtonReleased，兼容 SiemensS7 与 Leadshaine 双厂商）。
- `ISystemStateManager.cs`：定义系统状态管理抽象（CurrentState、StateChanged、ChangeStateAsync）。
- `Events/IoPanel/*.cs`：定义 IoPanel 按钮按下、急停释放、监控状态变更、故障四类事件载荷（`readonly record struct`）。
- `Events/System/StateChangeEventArgs.cs`：定义系统状态变更事件载荷（OldState/NewState/ChangedAt）。
- `IoPanelMonitoringStatus.cs`：定义 IoPanel 监控状态枚举（Stopped/Monitoring/Faulted）。
- `EmcControllerStatus.cs`：定义 EMC 控制器状态枚举及中文 Description。
- `LeadshaineEmcController.cs`：实现 Leadshaine EMC 初始化重试、volatile 分组快照轮询、`TryGetMonitoredPoint` 无锁单点查询、输出写入与断链重连。
- `LeadshaineEmcHardwareAdapter.cs`：封装 LTDMC 的初始化/读写/复位调用。
- `LeadshaineSensorManager.cs`：消费 EMC 快照并发布传感器状态事件，统一传感器监控状态流转。
- `LeadshaineIoPanel.cs`：实现 `IIoPanel`，消费 EMC 快照按 TriggerState 方向检测按下/释放边沿，按角色路由到对应事件（StartButtonPressed/StopButtonPressed 等），兼容 SiemensS7 同接口模式。
- `IoPanelButtonType.cs`：定义 IoPanel 按钮角色（Unspecified/Start/Stop/EmergencyStop/Reset），用于按钮语义配置与日志输出。
- `IoMonitoringHostedService.cs`（Execution）：面向 `IIoPanel` 与 `ISensorManager` 接口编排，统一 EMC 初始化、点位下发、IoPanel/Sensor 启停顺序。
- `IoPanelStateTransitionHostedService.cs`（Execution）：订阅 IoPanel 按钮事件并桥接到 `ISystemStateManager.ChangeStateAsync`，实现按钮驱动状态流转。
- `ChuteForcedRotationHostedService.cs`（Execution）：格口强排托管服务，支持轮转与固定两种互斥模式（轮转优先）；固定模式下订阅系统状态，Running 时闭合指定格口，非 Running 时自动断开。
- `SortingTaskOrchestrationService.cs`（Execution）：分拣主协调托管服务，负责包裹创建与成熟泵送，并协调上车/落格子服务。
- `SortingTaskCarrierLoadingService.cs`（Execution）：负责成熟包裹上车、绑定与队列回退。
- `SortingTaskDropOrchestrationService.cs`（Execution）：负责格口偏移映射命中后的落格执行与解绑回收。
- `LoopTrackManagerHostedService.cs`（Execution）：环轨连接、启动与状态监控托管流程。
- `LoopTrackHILHostedService.cs`（Execution）：环轨 HIL 联调托管流程。
- `LogCleanupHostedService.cs`（Execution）：日志保留期清理托管流程。
- `Execution/Properties/AssemblyInfo.cs`：声明 `InternalsVisibleTo("Zeye.NarrowBeltSorter.Core.Tests")`，用于测试访问 Execution 层内部方法，避免扩大生产可见性边界。
- `SensorWorkflowHelper.cs`：提供传感器点位同步到 EMC 与去抖窗口判定的通用能力。
- `IoBindingHelper.cs`：提供 IO 绑定配置通用解析（TriggerState 字符串 → IoState 枚举），跨厂商复用，消除 LeadshaineIoPanel 与 LeadshaineSensorManager 间的重复实现。
- `LeadshaineIoLinkageOptions.cs` 与 `LeadshaineIoLinkagePointOptions.cs`：定义联动 IO 配置模型（系统状态、点位、延迟、持续时长）。
- `LeadshainePointBindingOptionsValidator.cs`：补充 PortNo/BitNo 组合上限校验，防止输出位号溢出。
- `IoLinkageHostedService.cs`（Execution）：参考 WheelDiverterSorter 的联动语义，独立承载“系统状态 -> 输出点位”联动写入流程。
- `LocalSystemStateManager.cs`（Execution/Services/State）：提供 `ISystemStateManager` 的本地实现，向联动服务发布状态变更事件。
- `LeadshaineEmcControllerTestFactory.cs`：统一构造 EMC 控制器测试上下文，复用测试桩与默认配置。
- `Leadshaine/Emc/*Tests.cs`：覆盖初始化成功失败、输出写入边界、重连恢复、监控循环快照读取、断链检测与 `TryGetMonitoredPoint` 幂等注册等核心行为。
- `Leadshaine/Integration/FakeLeadshaineEmcController.cs`：提供托管服务与传感器管理器联调测试桩。
- `LeadshaineIoMonitoringHostedServiceTests.cs`：覆盖 EMC 初始化失败/成功时的托管服务编排行为。
- `Leadshaine/Integration/FakeSystemStateManager.cs`：提供联动服务状态变更测试桩。
- `LeadshaineIoLinkageHostedServiceTests.cs`：覆盖状态命中/未命中规则时的联动输出写入行为。
- `LeadshaineSensorManagerDebounceTests.cs`：覆盖传感器点位同步与去抖窗口事件抑制行为。
- `LeadshaineEmcConnectionOptionsTests.cs`：覆盖 EMC 连接参数合法值、边界值、关系约束与 IP 格式校验。
- `LeiMaModbusClientAdapter.cs`：提供雷码 Modbus TCP/RTU 读写封装，包含 Polly 重试超时策略与串口共享连接管理。
- `LeiMaSerialRtuSharedConnection.cs`：承载串口 RTU 共享连接状态与引用计数，支撑“单文件单类”约束下的共享连接复用。
- `Program.cs`：移除 `Transport` 分支与 `BuildServiceProvider` 风格提前构建，改用工厂 lambda 延迟创建适配器和管理器；当前仅注册单设备 `ZhiQianChuteManager`。
- `appsettings*.json`：智嵌配置改为 `Devices` 数组结构。
- `FakeZhiQianClientAdapter.cs` 与 `ZhiQianChuteManagerTests.cs`：同步替换为新接口与新配置结构。
- `多从站稳速难题分析与工程解决方案.md`：系统分析多从站闭环稳速不易收敛的 6 大根因，对比工业界主流方案（主从转矩跟随、虚拟主轴、下垂控制、交叉耦合控制、MPC）及代表产品，给出面向当前架构的阶段性改进建议。
- `Manager接口结构清单.md`：按 `Zeye.NarrowBeltSorter.Core/Manager` 目录维护接口树状图，用于接口增删改时的同步维护基准。
- `IIoPanel定义与联动IO服务两阶段实施计划.md`：对标 WheelDiverterSorter OnLine-Setting，输出 IIoPanel 定义+实现与联动 IO 服务的 2 PR 落地方案。
- `西门子S7实施计划（三个拉取请求落地）.md`：基于 WheelDiverterSorter OnLine-Setting 分支源码（提交 `6a5a618178bf9b3298dc4f7d4f3e1a71fabf4c71`），对 SiemensS7 的 `IEmcController` 与 `ISensorManager` 实现进行对标拆解，并给出三阶段落地路线图。
- `LeadshaineEmcController实施计划（三个拉取请求落地）.md`：基于 WheelDiverterSorter OnLine-Setting 分支源码（提交 `6a5a618178bf9b3298dc4f7d4f3e1a71fabf4c71`），对 LeadshaineEmcController 的实现机制进行对标拆解，并给出三阶段落地路线图。
- `红外参数生效与落格触发延迟分析.md`：基于当前代码链路给出红外参数实际生效边界、编码上下限、CarrierId 日志语义差异与“开闭格口触发延迟”成因拆解。

## 本次更新内容

- 新增 `红外参数生效与落格触发延迟分析.md`，覆盖红外参数生效链路与上下限、CarrierId 日志看似错号但落格正确的原因，以及开闭格口触发延迟的代码级原因分析与排查建议。
- 优化 `SignalTowerHostedService`：移除未使用的 `ISensorManager` 依赖，新增 `_startupWarningBuzzerCts` 实现启动预警蜂鸣可取消，状态切换时立即取消 `Task.Delay` 等待并关闭蜂鸣器，修复 `StartupWarning||Ready` 死代码分支，将事件订阅从构造函数迁移至 `ExecuteAsync`，标记 `sealed` 并补充 XML doc 注释。
- 重构 `Program.cs`：从约 220 行精简至 70 行，将所有静态注册函数提取为 `Vendors/DependencyInjection` 下的独立扩展类。
- 新增 `HostApplicationBuilderConfigurationExtensions.cs`：封装多层 JSON 配置文件加载（base → looptrack → chutes → leadshaine → Environment 覆盖），支持 `ZEYE_USE_ENV_ONLY_CONFIG` 环境变量跳过文件配置。
- 新增 `HostApplicationBuilderZhiQianExtensions.cs`：封装智嵌格口驱动注册（含格口校验、强排、落格模拟开关），`AddZhiQianChutes` 方法。
- 新增 `HostApplicationBuilderSortingExtensions.cs`：封装分拣任务编排服务注册，`AddSortingTaskOrchestration` 方法。
- 新增 `HostApplicationBuilderLoopTrackExtensions.cs`：封装环轨托管服务注册（正式/HIL），`AddLoopTrack` 方法。
- 扩展 `HostApplicationBuilderLeadshaineExtensions.cs`：新增 `AddLeadshaineIoMonitoring`、`AddLeadshaineIoPanelStateTransition`（含 `LeadshaineIoPanelStateTransitionOptions` 配置绑定）、`AddLeadshaineIoLinkage`、`AddLeadshaineCarrierLoopGrouping` 方法。
- 拆分 `appsettings.Development.json`：将 LoopTrack 迁移至 `appsettings.Development.looptrack.json`，将 Chutes + Carrier 迁移至 `appsettings.Development.chutes.json`，将 Leadshaine 迁移至 `appsettings.Development.leadshaine.json`，主文件精简为仅保留 `LogCleanup` + `Logging`。

- 修复 PR 审查与 CI 检查项：`LoopTrackManagerHostedService` 状态驱动改为“连接状态 + 运行状态”联合判定，避免断连失败后早退不重试；Running 且未连接时复用现有连接重试策略。
- 修复日志重复落盘：在 NLog `app-all` 兜底规则中补充 `ChuteDropSimulationHostedService` 的 Ignore，避免 `sorting-orchestration` 分类日志重复写入。
- 优化分拣日志性能与可读性：将“未到目标格口”和“靠近目标格口（1~2车）”降级为 Debug，降低高频 Information 日志写盘压力。
- 统一落格链路时间戳：`UnbindCarrierAsync` 复用 `droppedAt`，保持同一落格事务时间一致性。
- 收敛测试边界：新增 `Execution/Properties/AssemblyInfo.cs` 使用 `InternalsVisibleTo` 暴露 internal API 给测试，移除对生产方法可见性扩张。
- 新增并固化事件并行分发能力：在 `SafeExecutor` 增加 `PublishEventAsync`，用于“发布者快速返回 + 订阅者并行且互相隔离”的统一发布模式。
- 新增 `SafeExecutorPublishEventAsyncTests`，覆盖“发布端非阻塞 / 订阅者并行执行 / 异常订阅者隔离”三类并行分发专项验证。
- 将分拣编排拆分为“主协调 + 上车服务 + 落格服务”三层：`SortingTaskOrchestrationService` 负责协调，`SortingTaskCarrierLoadingService` 负责上车，`SortingTaskDropOrchestrationService` 负责落格，降低单服务耦合与体积。
- 更新 `LeadshaineEmcController`、`LeadshaineIoPanel`、`LeadshaineSensorManager`、`EmcSignalTower`、`InfraredSensorCarrierManager`、`InfraredSensorCarrier`、`ZhiQianChute`、`ZhiQianChuteManager`、`LocalSystemStateManager` 的事件发布路径，避免订阅者阻塞发布者与其他订阅者。
- 将强制规则写入 `.github/copilot-instructions.md`：事件订阅者禁止阻塞与相互影响，事件发布后订阅者必须并行获取。
- 恢复并纳管 `SortingTaskOrchestrationService.cs` 文件，修复 Host 层引用编译中断。
- 修复 `IoMonitoringHostedService` 监控点下发策略：启动时不再仅下发 IoPanel 按钮点位，而是下发“PointBindings 中全部 Input 点位 + IoPanel 点位”，避免出现仅单点被监控的问题。
- 修复分层依赖：`IoMonitoringHostedService` 改为依赖 Core 层 `LeadshaineIoPointBindingCollectionOptions`，避免 Execution 层直接引用 Drivers 配置类型。
- 更新 `LeadshaineIoMonitoringHostedServiceTests` 以匹配新的服务构造参数，确保监控编排测试可编译并继续覆盖启动链路。
- 新增 `IoPanelStateTransitionHostedService`：将 IoPanel 按钮事件桥接为系统状态变更（Start->Running、Stop->Paused、EmergencyStop->EmergencyStop、Reset->Booting、急停释放->Ready）。
- 更新 `Program.cs`：在 Leadshaine EMC 启用时注册 `IoPanelStateTransitionHostedService`。
- 新增 `IoPanelStateTransitionHostedServiceTests`：覆盖按钮事件到系统状态映射行为。
- 新增 `IoButtonLinkageEndToEndTests`：覆盖“按钮按下 -> 状态切换 -> IoLinkage 写 IO”端到端链路，验证 IO 联动闭环。
- 新增 `WheelDiverterSorter_OnLineSetting_IO按钮状态流转分析.md`，梳理 OnLine-Setting 中“按钮采样 -> 边沿识别 -> ChangeStateAsync -> 状态机约束”的端到端路径。
- 新增 `IoLinkageHostedService`（`Execution/Services/Hosted/IoLinkageHostedService.cs`），独立承载“系统状态 -> 输出点位”的联动能力，和 `IoMonitoringHostedService` 的监控能力分离。
- 新增 `StateChangeEventArgs`（`Core/Events/System/StateChangeEventArgs.cs`），补齐 `ISystemStateManager` 的状态变更事件载荷定义。
- 新增 `LocalSystemStateManager`（`Execution/Services/State/LocalSystemStateManager.cs`），为联动服务提供默认系统状态源实现。
- 新增 `LeadshaineIoLinkageOptions` 与 `LeadshaineIoLinkagePointOptions`（`Core/Options/Emc/Leadshaine`），支持在配置中声明联动规则。
- 更新 `Program.cs`：注册 `ISystemStateManager`、绑定 `Leadshaine:IoLinkage` 配置，并在启用时注册 `IoLinkageHostedService`。
- 更新 `appsettings.json` 与 `appsettings.Development.json`：新增 `Leadshaine:IoLinkage` 配置段及中文字段注释。
- 新增联动测试 `LeadshaineIoLinkageHostedServiceTests.cs` 与 `FakeSystemStateManager.cs`，并扩展 `FakeLeadshaineEmcController` 记录联动写入调用。
- 同步更新 `Manager接口结构清单.md`、`设备代码结构清单.md` 与 README 文件树及职责说明。

## 可继续完善项

1. 可在红外参数下发链路补充“编码失败/发送失败”结构化可观测日志（含失败参数快照），降低“日志显示成功但现场无变化”的排障成本。
2. 可补充事件发布压测与限流策略验证（高并发、多订阅者场景），评估吞吐量、背压行为与订阅侧稳定性。
3. 可为关键事件引入可观测指标（分发耗时、失败订阅者数量），便于线上快速定位订阅侧瓶颈。
4. 可增加配置开关（例如 `Leadshaine:EmcConnection:MonitorAllInputPoints`），在“仅监控业务点”与“监控全部输入点”之间按场景切换。
5. 可在启动日志中打印最终监控点全集（PointId 列表），方便现场快速核对配置是否生效。
6. 若后续引入真实系统状态流转编排，可将 `LocalSystemStateManager` 替换为业务态状态管理器实现，并保持 `IoLinkageHostedService` 仅依赖接口。
7. 可补充联动规则校验器（例如 PointId 必须为 Output、DelayMs/DurationMs 边界校验），在启动期提前发现配置错误。
8. 可把 WheelDiverterSorter 的按钮->系统状态时序图（含急停锁存与全释放判定）沉淀为统一厂商无关文档，降低跨项目迁移成本。
9. 可为 IoPanel 状态桥接补充可配置映射表（不同现场允许按钮到状态的自定义映射），减少硬编码策略改造成本。
10. 可继续补充 Stop/Reset/EmergencyStop 场景的端到端联动测试（含 DelayMs/DurationMs）以覆盖完整联动矩阵。
