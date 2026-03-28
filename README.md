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
│   └── Vendors/ZhiQian
│       ├── ZhiQianBinaryClientAdapter.cs   # 二进制写 + ASCII读，串行门控/重连重试
│       ├── ZhiQianChuteManager.cs          # 单设备格口管理器
│       ├── IZhiQianClientAdapterFactory.cs # 适配器工厂抽象
│       └── ZhiQianClientAdapterFactory.cs  # 默认工厂实现
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
  - 读用 ASCII 命令 `zq {addr} get y qz`，TouchSocket `AddTcpReceivedPlugin` 将收到的数据追加到 `StringBuilder`，按 `qz` 帧尾切片写入 `Channel<string>`；
  - 读超时重试时先重连并清空缓冲，避免幽灵应答污染。
- `Program.cs`：移除 `Transport` 分支与 `BuildServiceProvider` 风格提前构建，改用工厂 lambda 延迟创建适配器和管理器；当前仅注册单设备 `ZhiQianChuteManager`。
- `appsettings*.json`：智嵌配置改为 `Devices` 数组结构。
- `FakeZhiQianClientAdapter.cs` 与 `ZhiQianChuteManagerTests.cs`：同步替换为新接口与新配置结构。

## 本次更新内容

1. 删除 Modbus 专有接口与实现命名，切换到协议无关接口 + 二进制客户端。
2. 删除 `ZhiQianTransport` 传输枚举与相关分支。
3. 引入 `Devices` 数组配置模型（当前限制仅 1 台设备，先满足单设备落地）。
4. 简化 Host 注册流程为工厂化延迟创建。
5. 同步更新配置样例与测试桩/测试代码。
6. 增加旧版单设备配置兼容：如果未配置 `Devices`，会自动把顶层 `Host/Port/DeviceAddress/ChuteToDoMap` 迁移到 `Devices[0]`。
7. 同步更新 `NLog.config` 适配器 logger 名称与智嵌手册解析文档描述，统一为二进制客户端实现。

## 可继续完善项

1. 增加针对批写 checksum、ASCII 读帧解析、重连重试的独立单元测试与集成测试。
2. 后续恢复多设备支持时，再引入复合管理器与跨设备压测用例。
3. 在恢复多设备支持后，补充跨设备强排全局唯一约束与回归测试。
