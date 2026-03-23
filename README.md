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

- 新增 `.github/copilot-instructions.md`，写入指定 Copilot 限制规则与 PR 门禁条款。
- 新增两份中文梳理文档：
  - `雷码LM1000H说明书参数与调用逻辑梳理.md`
  - `雷码快速调机参数变频器配置表梳理.md`
- 修复一批与规则检查直接相关的问题：
  - 统一事件/实时模型中的时间类型为本地时间语义（`DateTime`）。
  - 修复 `Zeye.LoopSorter` 错误命名空间为 `Zeye.NarrowBeltSorter`。
  - 为 `SpeedAggregateStrategy` 枚举补齐 `Description`。
  - 清理重复/错误 using，并补齐缺失 using 以恢复类型可见性。

## 后续可完善点

- 在不引入额外侵入的前提下，补齐 Core 层当前缺失的类型定义（如 `LoopTrackConnectionOptions`、`LoopTrackPidOptions`），解除现有全量构建阻塞。
- 按模块补充自动化测试，尤其是事件载荷与时间语义相关单元测试。
- 在 Host 层补充 NLog 显式配置与性能策略验证，确保高频日志场景稳定。
