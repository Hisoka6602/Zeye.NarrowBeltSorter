# Zeye.NarrowBeltSorter

## 项目文件树（核心）

```text
Zeye.NarrowBeltSorter.sln
├── .github/workflows/cleanup-copilot-codex-branches.yml # 自动清理名称含 copilot/codex 的远程分支工作流（手动+定时触发）
├── Manager接口结构清单.md                 # 按 Manager 目录分章节维护接口结构树状图
├── 设备代码结构清单.md                    # 按设备分章节维护设备代码结构树状图
├── IIoPanel定义与联动IO服务两阶段实施计划.md # 对标 WheelDiverterSorter 的 IIoPanel 与联动 IO 服务 2 PR 落地计划
├── 西门子S7实施计划（三个拉取请求落地）.md  # 对标 WheelDiverterSorter 的 SiemensS7 实现并给出三阶段落地计划
├── LeadshaineEmcController实施计划（三个拉取请求落地）.md  # 对标 WheelDiverterSorter 的 LeadshaineEmcController 实现并给出三阶段落地计划
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
│   └── Services
│       ├── ChuteSelfHandlingHostedService.cs # 格口自处理托管编排服务
│       ├── ChuteForcedRotationHostedService.cs # 格口强排轮转托管编排服务
│       ├── LoopTrackManagerHostedService.cs # 环轨托管编排服务
│       ├── LoopTrackHILHostedService.cs # 环轨 HIL 托管编排服务
│       ├── LogCleanupHostedService.cs # 日志清理托管编排服务
│       └── Hosted/IoLinkageHostedService.cs # Leadshaine 联动 Io 托管编排服务
├── Zeye.NarrowBeltSorter.Host
│   ├── Program.cs                          # 服务注册与单设备装配入口（依赖 Execution 编排服务）
│   ├── Vendors/DependencyInjection/HostApplicationBuilderLeadshaineExtensions.cs # Leadshaine 配置注册入口
│   ├── Vendors/DependencyInjection/LeadshaineOptionsDelegateValidator.cs # Leadshaine 启动校验委托适配器
│   ├── appsettings.json                    # 生产默认配置（Devices 数组）
│   └── appsettings.Development.json        # 开发配置（Devices 数组）
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
    │       ├── LeadshaineIoLinkageHostedServiceTests.cs # IoLinkageHostedService 编排链路测试
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
- `Events/IoPanel/*.cs`：定义 IoPanel 按钮按下、急停释放、监控状态变更、故障四类事件载荷（`readonly record struct`）。
- `IoPanelMonitoringStatus.cs`：定义 IoPanel 监控状态枚举（Stopped/Monitoring/Faulted）。
- `EmcControllerStatus.cs`：定义 EMC 控制器状态枚举及中文 Description。
- `LeadshaineEmcController.cs`：实现 Leadshaine EMC 初始化重试、volatile 分组快照轮询、`TryGetMonitoredPoint` 无锁单点查询、输出写入与断链重连。
- `LeadshaineEmcHardwareAdapter.cs`：封装 LTDMC 的初始化/读写/复位调用。
- `LeadshaineSensorManager.cs`：消费 EMC 快照并发布传感器状态事件，统一传感器监控状态流转。
- `LeadshaineIoPanel.cs`：实现 `IIoPanel`，消费 EMC 快照按 TriggerState 方向检测按下/释放边沿，按角色路由到对应事件（StartButtonPressed/StopButtonPressed 等），兼容 SiemensS7 同接口模式。
- `IoPanelButtonType.cs`：定义 IoPanel 按钮角色（Unspecified/Start/Stop/EmergencyStop/Reset），用于按钮语义配置与日志输出。
- `IoLinkageHostedService.cs`（Execution）：面向 `IIoPanel` 与 `ISensorManager` 接口编排，统一 EMC 初始化、点位下发、IoPanel/Sensor 启停顺序。
- `ChuteForcedRotationHostedService.cs`（Execution）：按固定间隔轮转强排格口。
- `LoopTrackManagerHostedService.cs`（Execution）：环轨连接、启动与状态监控托管流程。
- `LoopTrackHILHostedService.cs`（Execution）：环轨 HIL 联调托管流程。
- `LogCleanupHostedService.cs`（Execution）：日志保留期清理托管流程。
- `SensorWorkflowHelper.cs`：提供传感器点位同步到 EMC 与去抖窗口判定的通用能力。
- `IoBindingHelper.cs`：提供 IO 绑定配置通用解析（TriggerState 字符串 → IoState 枚举），跨厂商复用，消除 LeadshaineIoPanel 与 LeadshaineSensorManager 间的重复实现。
- `LeadshainePointBindingOptionsValidator.cs`：补充 PortNo/BitNo 组合上限校验，防止输出位号溢出。
- `LeadshaineEmcControllerTestFactory.cs`：统一构造 EMC 控制器测试上下文，复用测试桩与默认配置。
- `Leadshaine/Emc/*Tests.cs`：覆盖初始化成功失败、输出写入边界、重连恢复、监控循环快照读取、断链检测与 `TryGetMonitoredPoint` 幂等注册等核心行为。
- `Leadshaine/Integration/FakeLeadshaineEmcController.cs`：提供托管服务与传感器管理器联调测试桩。
- `LeadshaineIoLinkageHostedServiceTests.cs`：覆盖 EMC 初始化失败/成功时的托管服务编排行为。
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

## 本次更新内容

- 新增 `IIoPanel` 接口（`Core/Manager/IoPanel/IIoPanel.cs`），按角色分发事件（StartButtonPressed/StopButtonPressed/EmergencyStopButtonPressed/ResetButtonPressed/EmergencyStopButtonReleased），对标 WheelDiverterSorter 兼容 SiemensS7 与 Leadshaine 双厂商。
- 新增 IoPanel 事件载荷目录 `Core/Events/IoPanel`，包含按钮按下（IoPanelButtonPressedEventArgs）、急停释放（IoPanelButtonReleasedEventArgs）、监控状态变更、故障四类 `readonly record struct` 事件。
- 新增 `IoPanelMonitoringStatus` 枚举（`Core/Enums/Io/IoPanelMonitoringStatus.cs`）。
- 新增 `LeadshaineIoPanel`（`Drivers/Vendors/Leadshaine/Emc/LeadshaineIoPanel.cs`），实现 `IIoPanel`，按 TriggerState 方向检测按下/释放边沿，按角色路由到对应事件，首次采样防误触发；替代原 `LeadshaineIoPanelManager`。
- 删除 `LeadshaineIoPanelManager.cs`（已被 `LeadshaineIoPanel` 完整替代）。
- 更新 `IoLinkageHostedService` 依赖由具体类切换为 `IIoPanel` 接口，实现面向接口编排。
- 更新 Leadshaine DI 注册：`AddSingleton<IIoPanel>` 绑定至 `LeadshaineIoPanel`。
- 同步更新 Manager接口结构清单.md、设备代码结构清单.md 与 README 文件树及职责说明。

## 可继续完善项

1. 若后续引入 SiemensS7 IoPanel，实现同一 `IIoPanel` 抽象，按角色订阅按下/释放事件并复用联动 IO 服务编排链路。
2. 可增补 IoPanel 按钮边沿检测集成测试（覆盖首次采样防误触、按下/释放边沿、EmergencyStop 释放、状态流转与故障收敛）。
