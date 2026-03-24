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
│   ├── Algorithms/
│   │   ├── PidController设计规划.md
│   │   ├── PidControllerOptions.cs
│   │   ├── PidControllerInput.cs
│   │   ├── PidControllerState.cs
│   │   ├── PidControllerOutput.cs
│   │   └── PidController.cs
│   ├── Events/
│   ├── Manager/
│   ├── Models/
│   ├── Options/
│   │   ├── LogCleanup/
│   │   └── TrackSegment/
│   │       ├── LoopTrackConnectionOptions.cs
│   │       └── LoopTrackPidOptions.cs
│   └── Utilities/
├── Zeye.NarrowBeltSorter.Core.Tests/
│   └── PidControllerTests.cs
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
- `.github/scripts/validate_copilot_rules.py`：根据 `copilot-instructions.md` 编号规则执行 PR 合规校验（规则更新时同步生效）。
- `.github/workflows/copilot-rules-validate.yml`：PR 触发的 Copilot 规则校验工作流。
- `Zeye.NarrowBeltSorter.Core`：核心领域层，包含枚举、事件载荷、管理器接口、模型、选项与安全执行工具。
  - `Algorithms/PidController设计规划.md`：PID 纯计算器设计规划文档（mm/s→Hz），定义参数模型、计算流程与防积分饱和（anti-windup）策略。
  - `Algorithms/PidControllerOptions.cs`：PID 参数对象，包含系数、采样周期、限幅与滤波参数及合法性校验。
  - `Algorithms/PidControllerInput.cs`：PID 输入载荷，定义目标速度、实际速度与积分冻结标志。
  - `Algorithms/PidControllerState.cs`：PID 迭代状态，保存积分、上一帧误差与微分状态。
  - `Algorithms/PidControllerOutput.cs`：PID 输出载荷，包含命令频率、各项贡献与下一状态。
  - `Algorithms/PidController.cs`：PID 纯计算器实现，执行 mm/s→Hz 域计算、限幅与条件积分 anti-windup。
  - `Options/TrackSegment/LoopTrackConnectionOptions.cs`：环形轨道连接参数定义（从站地址、超时、重试）。
  - `Options/TrackSegment/LoopTrackPidOptions.cs`：环形轨道 PID 参数定义（Kp/Ki/Kd）。
- `Zeye.NarrowBeltSorter.Core.Tests`：核心算法单元测试项目。
  - `PidControllerTests.cs`：覆盖参数校验、首帧微分、输出限幅、anti-windup 与冻结积分行为。
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

- 新增 `Zeye.NarrowBeltSorter.Core/Algorithms/PidControllerOptions.cs`、`PidControllerInput.cs`、`PidControllerState.cs`、`PidControllerOutput.cs`、`PidController.cs`：
  - 完成 PID 纯计算器落地（mm/s 输入、Hz 域计算、Hz 命令输出）。
  - 按约定实现参数校验、首帧微分抑制、积分限幅与条件积分 anti-windup。
- 新增 `Zeye.NarrowBeltSorter.Core.Tests/PidControllerTests.cs` 并接入解决方案：
  - 补齐 6 个核心单测场景，覆盖参数边界与关键控制行为。
- 更新 `README.md` 文件树与职责说明，保持与仓库实际结构一致。

## 后续可完善点

- 在执行层接入 `PidController` 时补充参数热更新与轨道分段调参策略。
- 增加稳定性回归场景（长时间运行、噪声输入、边界切换）验证控制质量。
