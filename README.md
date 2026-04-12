# Zeye.NarrowBeltSorter

## 项目文件树（核心）

```text
Zeye.NarrowBeltSorter.sln
├── .github/workflows/cleanup-copilot-codex-branches.yml # 自动清理名称含 copilot/codex 的远程分支工作流（手动+定时触发）
├── Manager接口结构清单.md                 # 按 Manager 目录分章节维护接口结构树状图
├── 设备代码结构清单.md                    # 按设备分章节维护设备代码结构树状图
├── 落格精准度动态成熟延迟模型改造清单.md     # 落格精准度最小侵入改造与动态成熟延迟模型实施清单
├── 配置文件拆分分析.md                    # 配置文件按能力模块拆分的边界说明与加载顺序文档
├── LiteDB配置中心改造计划.md              # 配置改造计划：API 配置/校验/热更新 + LiteDB 持久化 + appsettings 收口
├── IIoPanel定义与联动IO服务两阶段实施计划.md # 对标 WheelDiverterSorter 的 IIoPanel 与联动 IO 服务 2 PR 落地计划
├── WheelDiverterSorter_OnLineSetting_IO按钮状态流转分析.md # 分析 OnLine-Setting 分支中 IoPanel 按钮触发系统状态变更的完整链路
├── WheelDiverterSorter_OnLineSetting_上游通信与目标格口实施计划.md # 分析 OnLine-Setting 分支上游通信、目标格口获取与反馈链路，并给出分拣任务导向的分阶段实施计划与验收清单
├── 西门子S7实施计划（三个拉取请求落地）.md  # 对标 WheelDiverterSorter 的 SiemensS7 实现并给出三阶段落地计划
├── LeadshaineEmcController实施计划（三个拉取请求落地）.md  # 对标 WheelDiverterSorter 的 LeadshaineEmcController 实现并给出三阶段落地计划
├── 格口102红外参数一致性与体感分析.md        # 核对格口102红外参数与当前实现一致性，并分析体感变化不明显原因
├── 红外参数生效与落格触发延迟分析.md       # 根目录分析文档：红外参数生效边界、CarrierId日志语义与落格触发延迟成因
├── 包裹密集导致上车小车号偏差分析.md        # 分析包裹高密度场景下上车小车号偏差的时序链路与验证建议
├── 包裹密集场景上车与落格触发误差归因分析.md  # 基于现场日志与代码链路判定“热路径时序抖动主导、时间计算次级影响”
├── SignalR接入与实时状态推送实施计划.md       # SignalR 无鉴权接入方案，覆盖连接首帧全量状态与五类主题实时推送规划
├── 长期运行优化与热更新支持清单.md          # 汇总全年运行优化项与当前不支持热更新配置清单
├── 逐文件代码健康检查方案（多PR执行）.md      # 逐文件全覆盖检查方案，支持按批次拆分多个PR并确保无遗漏
├── Zeye.NarrowBeltSorter.Core
│   ├── Algorithms
│   │   ├── PidController.cs                # PID 控制器纯计算器（读速度 mm/s，输出频率 Hz）
│   │   ├── PidControllerInput.cs           # PID 控制器输入值结构体（值语义，readonly record struct）
│   │   ├── PidControllerOutput.cs          # PID 控制器输出值结构体（值语义，readonly record struct）
│   │   └── PidControllerState.cs           # PID 控制器内部状态结构体（积分误差累计）
│   ├── Manager/Carrier
│   │   ├── ICarrier.cs                     # 载具实体抽象（编号/方向/载货状态/转向事件）
│   │   └── ICarrierManager.cs              # 载具管理器抽象（环道建环/小车集合/感应位/落格偏移）
│   ├── Manager/Chutes
│   │   ├── IChuteManager.cs                # 格口管理器统一抽象
│   │   ├── IZhiQianClientAdapter.cs        # 智嵌协议无关客户端接口
│   │   └── IZhiQianClientAdapterFactory.cs # 智嵌客户端适配器工厂接口
│   ├── Manager/InductionLane
│   │   └── IInductionLane.cs               # 单路供包台抽象（状态/事件/控制）
│   ├── Manager/TrackSegment
│   │   ├── ILoopTrackManager.cs            # 环轨管理器统一抽象（连接/启停/调速/事件）
│   │   ├── ILoopTrackManagerAccessor.cs    # 环轨管理器访问器抽象（跨服务共享实例引用与变更通知）
│   │   └── ILeiMaModbusClientAdapter.cs    # 雷码 Modbus 客户端适配器接口
│   ├── Manager/Emc
│   │   ├── IEmcController.cs               # EMC 控制器统一抽象（初始化/监控/写入/重连）
│   │   └── IEmcHardwareAdapter.cs          # EMC 硬件访问适配器抽象
│   ├── Manager/IoPanel
│   │   └── IIoPanel.cs                     # IoPanel 操作面板管理器抽象（按钮边沿检测与事件发布）
│   ├── Manager/Parcel
│   │   └── IParcelManager.cs               # 包裹管理器抽象（包裹创建/落格/移除/事件）
│   ├── Manager/Sensor
│   │   └── ISensorManager.cs               # 传感器管理器抽象（启停/状态事件）
│   ├── Manager/SignalTower
│   │   └── ISignalTower.cs                 # 信号塔抽象（三色灯/蜂鸣器/连接状态/控制方法）
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
│   │   ├── LeadshaineBitBindingOption.cs     # Leadshaine 物理位绑定定义
│   │   ├── LeadshainePointReferenceOptions.cs # Leadshaine 点位引用基础配置（IoPanel/Sensor 绑定的抽象基类）
│   │   ├── LeadshaineIoPanelButtonBindingCollectionOptions.cs # IoPanel 按钮绑定集合配置
│   │   ├── LeadshaineIoPanelButtonBindingOptions.cs # IoPanel 单按钮点位绑定配置（含角色类型）
│   │   ├── LeadshaineIoPanelStateTransitionOptions.cs # IoPanel 按钮到系统状态流转时序参数（启动预警时长）
│   │   ├── LeadshaineSensorBindingCollectionOptions.cs # 传感器绑定集合配置
│   │   ├── LeadshaineSensorBindingOptions.cs # 传感器单点位绑定配置（含去抖/轮询/类型解析）
│   │   └── LeadshaineSignalTowerOptions.cs   # Leadshaine 信号塔输出点位绑定配置
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
│   ├── Events/Carrier
│   │   ├── CarrierApproachingTargetChuteEventArgs.cs # 小车靠近目标格口即将分拣事件载荷
│   │   ├── CarrierPassedForcedChuteEventArgs.cs # 小车经过强排格口事件载荷
│   │   └── CarrierLoadStatusChangedEventArgs.cs # 小车载货状态变化事件载荷（含条码与落格上下文）
│   ├── Events/Parcel
│   │   └── ParcelDroppedEventArgs.cs # 包裹落格事件载荷（含当前感应区小车上下文）
│   ├── Options/Carrier
│   │   └── CarrierManagerOptions.cs        # 载具管理器配置（小车编号列表）
│   ├── Options/Chutes
│   │   ├── ZhiQianChuteOptions.cs          # 智嵌共享配置（含 Devices 列表）
│   │   ├── ZhiQianDeviceOptions.cs         # 单设备配置与逐台校验
│   │   └── ZhiQianLoggingOptions.cs        # 格口日志配置
│   ├── Options/Sorting
│   │   └── SortingTaskTimingOptions.cs     # 分拣任务时序配置（包裹成熟延迟、成熟起始来源、格口开关门间隔、链路阶段耗时告警阈值、上车触发滞后窗口）
│   ├── Utilities/ConfigurationValueHelper.cs # 通用配置值安全回退工具（非法值回落默认值）
│   ├── Utilities/Chutes/ZhiQianAddressMap.cs # DO 通道边界与索引校验
│   ├── Utilities/PointBindingReferenceValidator.cs # 点位引用绑定通用校验工具（跨厂商复用）
│   ├── Utilities/SensorWorkflowHelper.cs # 传感器监控工作流通用辅助（点位同步/去抖判定）
│   ├── Utilities/IoBindingHelper.cs      # IO 绑定配置通用解析工具（TriggerState 解析，跨厂商复用）
│   └── Utilities/SortingChainLatencyStats.cs # 分拣链路延迟滑动窗口统计工具（按密度分桶记录 P50/P95/P99 与误差率，线程安全）
├── Zeye.NarrowBeltSorter.Drivers
│   └── Vendors
│       ├── LeiMa/
│       │   ├── LeiMaModbusClientAdapter.cs            # 雷码 Modbus 客户端适配器（TCP/RTU + 重试超时 + 共享串口连接）
│       │   ├── LeiMaSerialRtuSharedConnection.cs      # 雷码串口 RTU 共享连接上下文（连接键/门控/引用计数）
│       │   ├── LeiMaLoopTrackManager.cs               # 雷码环道管理器（连接/启停/调速/PID 闭环/状态事件）
│       │   └── doc/
│       │       ├── 多从站稳速难题分析与工程解决方案.md  # 多从站闭环稳速根因拆解与工程解法对比
│       │       └── 雷赛红外参数边界与实时性链路排查.md  # 雷赛红外参数边界、换算公式、CarrierId语义与触发延迟链路排查
│       ├── Leadshaine/
│       │   ├── Infrared/
│       │   │   └── LeadshaineInfraredDriverFrameCodec.cs # LDC-FJ-RF 红外 8 字节帧编解码（D1~D4/99H）
│       │   ├── SignalTower/
│       │   │   └── EmcSignalTower.cs # Leadshaine 信号塔实现（三色灯/蜂鸣器/连接状态，基于 EMC 输出点位）
│       │   └── Validators/
│       │       ├── LeadshainePointBindingOptionsValidator.cs # PointId 唯一与地址合法性校验
│       │       ├── LeadshaineIoPanelButtonOptionsBindingValidator.cs # IoPanel 引用点位校验
│       │       └── LeadshaineSensorOptionsBindingValidator.cs # Sensor 引用点位校验
│       │   └── Emc/
│       │       ├── LTDMC.cs # 雷赛运动控制底层互操作封装（厂商 SDK P/Invoke 绑定，规则豁免）
│       │       ├── LtdmcHsCmpInfo.cs # 雷赛高速比较位置信息结构体（struct_hs_cmp_info，P/Invoke 绑定）
│       │       ├── LtdmcDmc3K5KOperate.cs # 雷赛 3K/5K 系列中断回调委托（DMC3K5K_OPERATE，P/Invoke 绑定）
│       │       ├── LtdmcPwmCurveCtrlPoint.cs # PWM 曲线控制点结构体（PwmCurve_CtrlPoint，P/Invoke 绑定）
│       │       ├── LtdmcDaCurveCtrlPoint.cs # DA 曲线控制点结构体（DaCurve_CtrlPoint，P/Invoke 绑定）
│       │       ├── LeadshaineEmcController.cs # Leadshaine EMC 控制器实现
│       │       ├── LeadshaineEmcHardwareAdapter.cs # Leadshaine EMC 硬件访问适配器实现
│       │       └── LeadshaineIoPanel.cs # Leadshaine IoPanel 实现（消费 EMC 快照，按钮边沿检测并发布事件）
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
│       ├── Carrier/
│       │   ├── InfraredSensorCarrierManager.cs # 红外感应器小车管理器（内存实现，热路径 O(1) 查询）
│       │   └── InfraredSensorCarrier.cs # 红外感应器小车实体（并发安全，载货状态/转向方向/事件发布）
│       ├── ChuteSelfHandlingHostedService.cs # 格口自处理托管编排服务
│       ├── ChuteForcedRotationHostedService.cs # 格口强排托管编排服务（轮转/固定双模式互斥）
│       ├── SortingTaskOrchestrationService.cs # 分拣主协调托管服务（包裹创建与成熟泵送、传感器事件有序通道、上车触发绑定与丢包判定）
│       ├── SortingTaskCarrierLoadingService.cs # 分拣上车编排服务（成熟队列消费、上车绑定、Carrier-Parcel映射、五段链路时刻记录与P50/P95/P99统计及误差率、卸货路径链路缓存清理）
│       ├── SortingTaskDropOrchestrationService.cs # 分拣落格编排服务（到位映射、落格执行、解绑回收、落格链路误差率记录）
│       ├── LoopTrackManagerHostedService.cs # 环轨托管编排服务
│       ├── LoopTrackHILHostedService.cs # 环轨 HIL 托管编排服务
│       ├── SignalTowerHostedService.cs # 信号塔托管服务（系统状态/建环/环轨管理器变更事件联动）
│       ├── LogCleanupHostedService.cs # 日志清理托管编排服务
│       ├── State/LocalSystemStateManager.cs # 本地系统状态管理器实现
│       ├── State/LoopTrackManagerAccessor.cs # 环轨管理器访问器实现（托管服务写入，消费服务读取）
│       └── Hosted
│           ├── IoMonitoringHostedService.cs # Leadshaine Io 监控托管编排服务
│           ├── IoPanelStateTransitionHostedService.cs # IoPanel 按钮到系统状态桥接托管服务（Start/Stop/急停/复位）
│           ├── IoLinkageHostedService.cs # Leadshaine 联动 Io 托管服务（系统状态到输出点位）
│           └── MaintenanceHostedService.cs # 检修托管服务（IoPanel 检修开关按钮驱动状态切换与检修速度控制）
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
│   ├── appsettings.json                    # 全环境统一基线配置（日志保留2天）
│   ├── appsettings.looptrack.json          # 全环境统一环轨基线（HIL 默认开启）
│   ├── appsettings.chutes.json             # 全环境统一格口基线（强排/落格模拟默认开启）
│   ├── appsettings.leadshaine.json         # 全环境统一 Leadshaine 基线（EMC/IoPanel/Sensor/SignalTower/IoLinkage）
│   ├── appsettings.devices.looptrack.json  # 全环境统一环轨设备参数（串口/从站等硬件参数）
│   └── appsettings.devices.chutes.json     # 全环境统一格口设备参数（IP/端口/格口映射/红外参数）
└── Zeye.NarrowBeltSorter.Core.Tests
    ├── FakeZhiQianClientAdapter.cs         # 智嵌客户端测试桩
    ├── FakeLoopTrackManagerAccessor.cs     # 环轨管理器访问器测试桩
    ├── OptionsMonitorTestHelper.cs         # IOptionsMonitor 测试工厂（Create<T> 工厂方法）
    ├── StaticOptionsMonitor.cs             # 固定值选项监视器实现（IOptionsMonitor 测试桩，不触发回调）
    ├── NullDisposable.cs                   # 无操作空释放对象单例（测试桩中返回 IDisposable 的轻量实现）
    ├── LogCleanupHostedServiceTests.cs     # 日志清理托管服务递归目录清理测试
    ├── SortingChainLatencyStatsTests.cs    # 分拣链路延迟统计工具单元测试（循环缓冲、分桶、百分位边界、误差率、并发）
    ├── ZhiQianChuteManagerTests.cs         # 格口管理器行为测试
    ├── Leadshaine/
    │   ├── LeadshaineEmcConnectionOptionsTests.cs # EMC 连接参数边界校验测试
    │   ├── SortingTaskOrchestrationReflectionTestHelper.cs # 分拣编排服务反射辅助工具——工厂与状态访问分部
    │   ├── SortingTaskOrchestrationReflectionTestHelper.Invoke.cs # 分拣编排服务反射辅助工具——私有方法调用分部
    │   ├── StubSystemStateManager.cs        # 固定系统状态测试桩（ISystemStateManager 实现，供编排单元测试注入）
    │   ├── SortingTaskOrchestrationMatureStartTests.cs # 分拣编排成熟起始来源与触发绑定行为测试
    │   ├── SortingTaskOrchestrationSensorChannelTests.cs # 分拣编排传感器事件通道行为测试（Phase 3.2：FIFO 顺序、关闭判别、满载计数）
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
- `Events/Carrier/*.cs`：定义小车建环、感应位变化、靠近目标格口、经过强排格口与载货状态变化事件载荷。
- `Events/Parcel/ParcelDroppedEventArgs.cs`：定义包裹落格事件载荷（含 `CurrentInductionCarrierId` 上下文）。
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
- `MaintenanceHostedService.cs`（Execution）：检修托管服务，监听 IoPanel 检修开关（MaintenanceSwitch）按下/释放事件；开关打开时切换至检修状态并以检修速度驱动轨道；开关关闭时停轨并切换至暂停状态；急停期间触发检修则蜂鸣 5 秒。
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
- `雷赛红外参数边界与实时性链路排查.md`：沉淀雷赛红外参数编码边界与换算公式（含 `RollerDiameterMm=67` 基准值）、上车位 `CarrierId` 语义核对，以及“离开很远才触发”的链路级排查项。
- `Manager接口结构清单.md`：按 `Zeye.NarrowBeltSorter.Core/Manager` 目录维护接口树状图，用于接口增删改时的同步维护基准。
- `IIoPanel定义与联动IO服务两阶段实施计划.md`：对标 WheelDiverterSorter OnLine-Setting，输出 IIoPanel 定义+实现与联动 IO 服务的 2 PR 落地方案。
- `WheelDiverterSorter_OnLineSetting_上游通信与目标格口实施计划.md`：对标 WheelDiverterSorter OnLine-Setting，梳理上游通信建连、目标格口获取、上报反馈链路，覆盖 Client/Server 双模式要点，并按本项目“分拣任务”语义给出分阶段实施计划、验收清单与待确认项。
- `西门子S7实施计划（三个拉取请求落地）.md`：基于 WheelDiverterSorter OnLine-Setting 分支源码（提交 `6a5a618178bf9b3298dc4f7d4f3e1a71fabf4c71`），对 SiemensS7 的 `IEmcController` 与 `ISensorManager` 实现进行对标拆解，并给出三阶段落地路线图。
- `LeadshaineEmcController实施计划（三个拉取请求落地）.md`：基于 WheelDiverterSorter OnLine-Setting 分支源码（提交 `6a5a618178bf9b3298dc4f7d4f3e1a71fabf4c71`），对 LeadshaineEmcController 的实现机制进行对标拆解，并给出三阶段落地路线图。
- `格口102红外参数一致性与体感分析.md`：基于仓库内现有代码与文档，核对格口 102 红外参数是否满足当前实现约束，并给出“速度/时间体感变化不大”的可追溯原因分析。
- `红外参数生效与落格触发延迟分析.md`：基于当前代码链路给出红外参数实际生效边界、编码上下限、CarrierId 日志语义差异与“开闭格口触发延迟”成因拆解。
- `包裹密集导致上车小车号偏差分析.md`：聚焦“包裹越密集上车小车号越偏差”问题，拆解编号更新与上车映射的时序放大机制并给出验证建议。
- `包裹密集场景上车与落格触发误差归因分析.md`：基于现场日志与当前实现，判断“上车/落格不准”主要由热路径时序抖动放大导致，并区分时间计算的次级影响。
- `SignalR接入与实时状态推送实施计划.md`：基于现有实时发布契约与编排服务，规划 SignalR 无鉴权接入、Worker Host（Generic Host）与 Web Host（Web 承载层）承载路径、连接首帧全量下发、五类主题实时增量推送与安全边界。
- `LogCleanupHostedServiceTests.cs`：覆盖日志清理服务对分类子目录的过期日志递归清理行为。
- `长期运行优化与热更新支持清单.md`：记录长期运行优化建议，并按代码现状列出不支持热更新项及原因。
- `LiteDB配置中心改造计划.md`：定义配置中心改造目标、分阶段实施路径与验收清单，统一后续配置变更走 API + LiteDB 持久化 + 热更新链路，收口 `appsettings.json` 仅保留 `LogCleanup`。
- `逐文件代码健康检查方案（多PR执行）.md`：定义逐文件全覆盖检查口径、记录模板、分批执行策略与通用验收清单，支撑“仅检查不改码”的审查型 PR 交付。

## 本次更新内容

- 更新 `Manager接口结构清单.md`：补全 `ISignalTower.cs`、`IParcelManager.cs`、`ICarrierManager.cs`、`ICarrier.cs` 四个接口的实现文件与使用文件，消除 `(暂无实现)` 占位。
- 更新 `设备代码结构清单.md`：Leadshaine 章节补全 `SignalTower/EmcSignalTower.cs`、`LeadshaineIoPanelStateTransitionOptions.cs`、`LeadshaineSignalTowerOptions.cs`，Host 节修正树形符号并补充 `SignalTowerHostedService.cs` 条目。
- 更新 `README.md` 文件树：补全 `Core/Algorithms/`（PidController 系列）、`Core/Manager/Carrier/`、`Core/Manager/Parcel/`、`Core/Manager/SignalTower/`、`Core/Options/Carrier/`、`Drivers/LeiMa/LeiMaLoopTrackManager.cs`、`Drivers/Leadshaine/SignalTower/EmcSignalTower.cs`、`Execution/Services/Carrier/` 等缺失条目，确保文件树与仓库实际内容一致。

## 后续可完善点

- 补充 PidController 对变频器调速的集成测试（`LeiMaLoopTrackManager` 闭环验证）。
- 待 `IInductionLane`、`IDeviceRealtimePublisher` 实现落地后同步更新两个结构清单。
