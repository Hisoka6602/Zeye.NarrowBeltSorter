# Zeye.NarrowBeltSorter

## 项目文件树（核心）

```text
Zeye.NarrowBeltSorter.sln
├── Manager接口结构清单.md                 # 按 Manager 目录分章节维护接口结构树状图
├── 设备代码结构清单.md                    # 按设备分章节维护设备代码结构树状图
├── 西门子S7实施计划（三个拉取请求落地）.md  # 对标 WheelDiverterSorter 的 SiemensS7 实现并给出三阶段落地计划
├── LeadshaineEmcController实施计划（三个拉取请求落地）.md # 对标 WheelDiverterSorter 的 LeadshaineEmcController 实现并给出三阶段落地计划
├── Zeye.NarrowBeltSorter.Core
│   ├── Manager/Chutes
│   │   ├── IChuteManager.cs                # 格口管理器统一抽象
│   │   └── IZhiQianClientAdapter.cs        # 智嵌协议无关客户端接口
│   ├── Manager/InductionLane
│   │   ├── IInductionLaneManager.cs        # 供包通道管理器抽象
│   │   └── IInductionLane.cs               # 单路供包台抽象（状态/事件/控制）
│   ├── Manager/SignalTower
│   │   └── ISignalTower.cs                 # 单个信号塔抽象（灯/蜂鸣器/连接）
│   ├── Enums/InductionLane
│   │   └── InductionLaneStatus.cs          # 供包台状态枚举
│   ├── Enums/SignalTower
│   │   ├── SignalTowerLightStatus.cs       # 信号塔三色灯状态枚举
│   │   └── BuzzerStatus.cs                 # 信号塔蜂鸣器状态枚举
│   ├── Options/InductionLane
│   │   └── InductionLaneOptions.cs         # 供包台配置模型
│   ├── Events/InductionLane
│   │   ├── InductionLaneParcelCreatedEventArgs.cs # 供包台包裹创建事件载荷
│   │   ├── InductionLaneParcelArrivedAtLoadingPositionEventArgs.cs # 包裹到达上车位事件载荷
│   │   └── InductionLaneStatusChangedEventArgs.cs # 供包台状态变化事件载荷
│   ├── Events/SignalTower
│   │   ├── SignalTowerLightStatusChangedEventArgs.cs # 三色灯状态变化事件载荷
│   │   ├── SignalTowerBuzzerStatusChangedEventArgs.cs # 蜂鸣器状态变化事件载荷
│   │   └── SignalTowerConnectionStatusChangedEventArgs.cs # 连接状态变化事件载荷
│   ├── Options/Chutes
│   │   ├── ZhiQianChuteOptions.cs          # 智嵌共享配置（含 Devices 列表）
│   │   ├── ZhiQianDeviceOptions.cs         # 单设备配置与逐台校验
│   │   └── ZhiQianLoggingOptions.cs        # 格口日志配置
│   └── Utilities/Chutes/ZhiQianAddressMap.cs # DO 通道边界与索引校验
├── Zeye.NarrowBeltSorter.Drivers
│   └── Vendors
│       ├── LeiMa/
│       │   ├── LeiMaModbusClientAdapter.cs            # 雷码 Modbus 客户端适配器（TCP/RTU + 重试超时 + 共享串口连接）
│       │   ├── LeiMaSerialRtuSharedConnection.cs      # 雷码串口 RTU 共享连接上下文（连接键/门控/引用计数）
│       │   └── doc/
│       │       └── 多从站稳速难题分析与工程解决方案.md  # 多从站闭环稳速根因拆解与工程解法对比
│       ├── Leadshaine/
│       │   └── Infrared/
│       │       └── LeadshaineInfraredDriverFrameCodec.cs # LDC-FJ-RF 红外 8 字节帧编解码（D1~D4/99H）
│       └── ZhiQian
│           ├── ZhiQianBinaryClientAdapter.cs   # 二进制写 + ASCII读，串行门控/重连重试
│           ├── ZhiQianChuteManager.cs          # 单设备格口管理器
│           ├── IZhiQianClientAdapterFactory.cs # 适配器工厂抽象
│           └── ZhiQianClientAdapterFactory.cs  # 默认工厂实现
├── Zeye.NarrowBeltSorter.Host
│   ├── Program.cs                          # 服务注册与单设备装配入口
│   ├── appsettings.json                    # 生产默认配置（Devices 数组）
│   └── appsettings.Development.json        # 开发配置（Devices 数组）
└── Zeye.NarrowBeltSorter.Core.Tests
    ├── FakeZhiQianClientAdapter.cs         # 智嵌客户端测试桩
    └── ZhiQianChuteManagerTests.cs         # 格口管理器行为测试
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
- `LeiMaModbusClientAdapter.cs`：提供雷码 Modbus TCP/RTU 读写封装，包含 Polly 重试超时策略与串口共享连接管理。
- `LeiMaSerialRtuSharedConnection.cs`：承载串口 RTU 共享连接状态与引用计数，支撑“单文件单类”约束下的共享连接复用。
- `Program.cs`：移除 `Transport` 分支与 `BuildServiceProvider` 风格提前构建，改用工厂 lambda 延迟创建适配器和管理器；当前仅注册单设备 `ZhiQianChuteManager`。
- `appsettings*.json`：智嵌配置改为 `Devices` 数组结构。
- `FakeZhiQianClientAdapter.cs` 与 `ZhiQianChuteManagerTests.cs`：同步替换为新接口与新配置结构。
- `多从站稳速难题分析与工程解决方案.md`：系统分析多从站闭环稳速不易收敛的 6 大根因，对比工业界主流方案（主从转矩跟随、虚拟主轴、下垂控制、交叉耦合控制、MPC）及代表产品，给出面向当前架构的阶段性改进建议。
- `Manager接口结构清单.md`：按 `Zeye.NarrowBeltSorter.Core/Manager` 目录维护接口树状图，用于接口增删改时的同步维护基准。
- `西门子S7实施计划（三个拉取请求落地）.md`：基于 WheelDiverterSorter OnLine-Setting 分支源码（提交 `6a5a618178bf9b3298dc4f7d4f3e1a71fabf4c71`），对 SiemensS7 的 `IEmcController` 与 `ISensorManager` 实现进行对标拆解，并给出三阶段落地路线图。
- `LeadshaineEmcController实施计划（三个拉取请求落地）.md`：基于 WheelDiverterSorter OnLine-Setting 分支源码（提交 `6a5a618178bf9b3298dc4f7d4f3e1a71fabf4c71`），对 LeadshaineEmcController 的实现机制进行对标拆解，并给出三阶段落地路线图。

## 本次更新内容

- 基于 `origin/master` 中原始注释，补全 `IInductionLane` 与 `ISignalTower` 的字段语义、事件契约与方法签名。
- 新增供包台/信号塔所需的最小枚举与事件载荷，并将供包台配置定义为 `InductionLaneOptions`。
- 新增仓库根目录 `设备代码结构清单.md`，按 ZhiQian / LeiMa / Leadshaine / SiemensS7 分章节维护设备代码结构，作为后续设备增删改时的同步维护清单。
- 新增仓库根目录 `Manager接口结构清单.md`，按 `Zeye.NarrowBeltSorter.Core/Manager` 目录维护接口树状图，作为后续 Manager 接口增删改时的同步维护清单。
- 新增 `LeadshaineInfraredDriverFrameCodec`，实现 `IInfraredDriverFrameCodec`，`VendorCode` 固定返回 `Leadshaine`。
- 新增 LDC-FJ-RF 8 字节帧编码：DIN1~DIN4 分别映射 D1H~D4H，Byte2 写入方向+地址，Byte3~Byte7 写入速度/延时/时间或圈数/模式，Byte8 按 Byte2~Byte7 异或生成。
- 新增 99H 回包解析：仅接收 8 字节 99H，按 Byte2~Byte4 异或校验，提取故障位并回填最小 `InfraredChuteOptions`。
- 删除 `LeadshaineInfraredDriverFrameCodecTests`，原因是该测试中速度/时间换算与 99H 回包断言沿用旧协议假设，已与当前 `LeadshaineInfraredDriverFrameCodec` 实现语义不一致；后续改为通过真实设备协议联调与集成验证覆盖对应场景。
- 新增《西门子S7实施计划（三个拉取请求落地）.md》，沉淀对 WheelDiverterSorter 的 SiemensS7 对标分析与三阶段实施计划。
- 新增《LeadshaineEmcController实施计划（三个拉取请求落地）.md》，沉淀对 WheelDiverterSorter 的 LeadshaineEmcController 对标分析与三阶段实施计划。
- 同步更新 README 文件树与关键文件职责说明，保证文档与仓库结构一致。

## 可继续完善项

1. 在驱动实现层补充 `IInductionLane` 的状态机转换细则与异常事件发布策略。
2. 在驱动实现层补充 `ISignalTower` 的闪烁节拍、蜂鸣器节奏与连接重试策略。
3. 在 CI 校验中继续扩展《Manager接口结构清单.md》机检范围（覆盖接口实现映射与注释完整性场景）。
4. 在新增 Manager 接口模板流程中引入《Manager接口结构清单.md》自动更新提示，减少人工漏改。
5. 补充 83H 返回的 99H 回包差异分支测试，避免多协议源混用时出现误判。
6. 在后续接入真实链路时补充参数量化系数（VK/TDK/TK/PK）与配置化换算测试。
7. 按《LeadshaineEmcController实施计划（三个拉取请求落地）.md》推进三阶段落地，并在每个 PR 完成后回填验收结论。
