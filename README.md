# Zeye.NarrowBeltSorter

## 项目文件树（核心）

```text
Zeye.NarrowBeltSorter.sln
├── Zeye.NarrowBeltSorter.Core
│   ├── Manager/Chutes
│   │   ├── IChuteManager.cs                # 格口管理器统一抽象
│   │   └── IZhiQianClientAdapter.cs        # 智嵌协议无关客户端接口
│   ├── Options/Chutes
│   │   ├── ZhiQianChuteOptions.cs          # 智嵌共享配置（含 Devices 列表）
│   │   ├── ZhiQianDeviceOptions.cs         # 单设备配置与逐台校验
│   │   └── ZhiQianLoggingOptions.cs        # 格口日志配置
│   └── Utilities/Chutes/ZhiQianAddressMap.cs # DO 通道边界与索引校验
├── Zeye.NarrowBeltSorter.Drivers
│   └── Vendors
│       ├── LeiMa/
│       │   └── doc/
│       │       └── 多从站稳速难题分析与工程解决方案.md  # 多从站闭环稳速根因拆解与工程解法对比
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
- `Program.cs`：移除 `Transport` 分支与 `BuildServiceProvider` 风格提前构建，改用工厂 lambda 延迟创建适配器和管理器；当前仅注册单设备 `ZhiQianChuteManager`。
- `appsettings*.json`：智嵌配置改为 `Devices` 数组结构。
- `FakeZhiQianClientAdapter.cs` 与 `ZhiQianChuteManagerTests.cs`：同步替换为新接口与新配置结构。
- `多从站稳速难题分析与工程解决方案.md`：系统分析多从站闭环稳速不易收敛的 6 大根因，对比工业界主流方案（主从转矩跟随、虚拟主轴、下垂控制、交叉耦合控制、MPC）及代表产品，给出面向当前架构的阶段性改进建议。

## 本次更新内容

- 修复智嵌 ASCII 回读在部分设备响应格式下的超时问题：新增 `TryExtractFrame` 解析逻辑，支持 `qz` 结尾和 `\n` 换行双通道切帧。
- 对换行切帧增加 `IsPossibleAsciiResponse` 过滤，避免把噪声文本写入读通道导致误判。
- 保留原有“ASCII读帧 + Channel消费 + 轮询同步 + 写后读校验”主链路，不改变业务接口。

## 可继续完善项

1. 增加针对批写 checksum、ASCII 读帧解析、重连重试的独立单元测试与集成测试。
2. 后续恢复多设备支持时，再引入复合管理器与跨设备压测用例。
3. 在恢复多设备支持后，补充跨设备强排全局唯一约束与回归测试。
4. 为 `TryExtractFrame` 增加专门单元测试（`qz` 结尾、仅换行结尾、分包/粘包、噪声混入）以降低现场协议差异风险。
