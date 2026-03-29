# Zeye.NarrowBeltSorter

## 项目文件树（核心）

```text
Zeye.NarrowBeltSorter.sln
├── Zeye.NarrowBeltSorter.Core
│   ├── Manager/Chutes
│   │   ├── IChuteManager.cs                # 格口管理器统一抽象
│   │   └── IZhiQianClientAdapter.cs        # 智嵌协议无关客户端接口
│   ├── Manager/InductionLane
│   │   ├── IInductionLaneManager.cs        # 供包通道管理器抽象
│   │   └── IInductionLane.cs               # 单路供包通道抽象
│   ├── Manager/SignalTower
│   │   └── ISignalTower.cs                 # 单个信号塔抽象
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
    ├── ZhiQianChuteManagerTests.cs         # 格口管理器行为测试
    └── LeadshaineInfraredDriverFrameCodecTests.cs # Leadshaine 红外帧编码/校验/故障位解析测试
```

## 各关键文件实现说明

- `IZhiQianClientAdapter.cs`：抽象连接、读 32 路状态、单写、批写能力，解耦具体协议实现。
- `IInductionLane.cs`：定义单路供包通道最小能力（标识、名称、启停状态与异步启停控制）。
- `ISignalTower.cs`：定义单个信号塔最小能力（标识、名称、启停状态与异步启停控制）。
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
- `LeadshaineInfraredDriverFrameCodecTests.cs`：覆盖编码成功、99H 校验失败、99H 故障位解析三类核心场景。
- `LeiMaModbusClientAdapter.cs`：提供雷码 Modbus TCP/RTU 读写封装，包含 Polly 重试超时策略与串口共享连接管理。
- `LeiMaSerialRtuSharedConnection.cs`：承载串口 RTU 共享连接状态与引用计数，支撑“单文件单类”约束下的共享连接复用。
- `Program.cs`：移除 `Transport` 分支与 `BuildServiceProvider` 风格提前构建，改用工厂 lambda 延迟创建适配器和管理器；当前仅注册单设备 `ZhiQianChuteManager`。
- `appsettings*.json`：智嵌配置改为 `Devices` 数组结构。
- `FakeZhiQianClientAdapter.cs` 与 `ZhiQianChuteManagerTests.cs`：同步替换为新接口与新配置结构。
- `多从站稳速难题分析与工程解决方案.md`：系统分析多从站闭环稳速不易收敛的 6 大根因，对比工业界主流方案（主从转矩跟随、虚拟主轴、下垂控制、交叉耦合控制、MPC）及代表产品，给出面向当前架构的阶段性改进建议。

## 本次更新内容

- 新增 `Zeye.NarrowBeltSorter.Core/Manager/InductionLane/IInductionLane.cs`，补全单路供包通道接口定义。
- 新增 `Zeye.NarrowBeltSorter.Core/Manager/SignalTower/ISignalTower.cs`，补全信号塔接口定义。
- 同步更新 README 文件树与关键文件职责说明，保持文档与仓库结构一致。

## 可继续完善项

1. 后续可按设备协议扩展 `IInductionLane`（如供包请求、在位检测、拥堵状态）并补齐对应事件载荷。
2. 后续可按三色灯/蜂鸣器模型扩展 `ISignalTower`（如分通道状态控制、闪烁节拍）并补齐对应枚举与事件。
