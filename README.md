# Zeye.NarrowBeltSorter

## 文件树与职责说明

```text
.
├── .github/
│   └── copilot-instructions.md
├── Zeye.NarrowBeltSorter.Core/
│   ├── Enums/
│   ├── Events/
│   ├── Manager/
│   ├── Models/
│   ├── Options/
│   │   ├── LogCleanup/
│   │   └── TrackSegment/
│   │       ├── LoopTrackConnectionOptions.cs
│   │       └── LoopTrackPidOptions.cs
│   └── Utilities/
├── Zeye.NarrowBeltSorter.Drivers/
│   └── Vendors/
│       └── LeiMa/
│           └── doc/
│               ├── 2-LM1000H 说明书.pdf
│               ├── (雷码)快速调机参数20250826.xlsx
│               ├── 雷码LM1000H说明书参数与调用逻辑梳理.md
│               └── 雷码快速调机参数变频器配置表梳理.md
├── Zeye.NarrowBeltSorter.Execution/
├── Zeye.NarrowBeltSorter.Host/
├── Zeye.NarrowBeltSorter.Infrastructure/
├── Zeye.NarrowBeltSorter.Ingress/
└── Zeye.NarrowBeltSorter.sln
```

- `.github/copilot-instructions.md`：Copilot 代码与交付约束规则。
- `Zeye.NarrowBeltSorter.Core`：核心领域层，包含枚举、事件载荷、管理器接口、模型、选项与安全执行工具。
  - `Options/TrackSegment/LoopTrackConnectionOptions.cs`：环形轨道连接参数定义（从站地址、超时、重试）。
  - `Options/TrackSegment/LoopTrackPidOptions.cs`：环形轨道 PID 参数定义（Kp/Ki/Kd）。
- `Zeye.NarrowBeltSorter.Drivers`：设备驱动与厂商资料。
  - `Vendors/LeiMa/doc/2-LM1000H 说明书.pdf`：雷码 LM1000H 原始说明书。
  - `Vendors/LeiMa/doc/(雷码)快速调机参数20250826.xlsx`：雷码快速调机参数原始表。
  - `Vendors/LeiMa/doc/雷码LM1000H说明书参数与调用逻辑梳理.md`：从说明书提取的参数与调用逻辑梳理（含出处页码）。
  - `Vendors/LeiMa/doc/雷码快速调机参数变频器配置表梳理.md`：从调机参数表提取的变频器配置参数梳理。
- `Zeye.NarrowBeltSorter.Execution`：执行层（流程/调度相关）。
- `Zeye.NarrowBeltSorter.Host`：宿主程序与后台服务（如日志清理服务）。
- `Zeye.NarrowBeltSorter.Infrastructure`：基础设施层。
- `Zeye.NarrowBeltSorter.Ingress`：入口与接入层。
- `Zeye.NarrowBeltSorter.sln`：解决方案文件。

## 本次更新内容

- 重写 `Zeye.NarrowBeltSorter.Drivers/Vendors/LeiMa/doc/雷码LM1000H说明书参数与调用逻辑梳理.md`：
  - 仅保留可在 `2-LM1000H 说明书.pdf` 中定位的参数与调用逻辑。
  - 增补面向 `ILoopTrackManager` 的接入映射（建链、启停、速度给定、状态轮询、故障复位）。
- 新增 `Zeye.NarrowBeltSorter.Core/Options/TrackSegment` 下两份参数文件：
  - `LoopTrackConnectionOptions.cs`
  - `LoopTrackPidOptions.cs`
- 在 `ILoopTrackManager` 中补齐 `using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;`，恢复接口类型可见性并解除构建阻塞。

## 后续可完善点

- 在 `ILoopTrackManager` 实现层落地 LM1000H 的 Modbus 映射细节（2000H/3000H/3100H/4000H）与异常日志策略。
- 补充面向驱动接入的集成测试（通讯参数不一致、超时、故障复位、状态轮询）。
- 按现场设备型号补充参数模板，减少联调阶段手工配置成本。
