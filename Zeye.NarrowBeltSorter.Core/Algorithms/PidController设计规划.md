# PidController（变频器稳速 Hz 给定）设计规划

## 1. 目标与边界

- 目标：在 `Zeye.NarrowBeltSorter.Core.Algorithms` 命名空间定义 `PidController`，作为**纯计算器**输出下一次频率给定（Hz），用于稳速控制。
- 边界：
  - `PidController` 只做数学计算，不直接访问驱动、通信、日志、时间源、线程、配置中心。
  - 外部调用方负责采样值获取（实际速度）、参数下发（写变频器 Hz）、异常捕获与日志。
  - 单位统一使用 `decimal`，避免跨层精度口径不一致。

## 2. 建议文件与类型

> 以下为规划结构，后续实现可按同名文件落地。

1. `PidController.cs`
   - `public sealed class PidController`
2. `PidControllerOptions.cs`
   - `public sealed record PidControllerOptions`
3. `PidControllerState.cs`
   - `public readonly record struct PidControllerState`
4. `PidControllerInput.cs`
   - `public readonly record struct PidControllerInput`
5. `PidControllerOutput.cs`
   - `public readonly record struct PidControllerOutput`

说明：状态、输入、输出使用 `readonly record struct`，保证不可变和值语义，便于高频调用与低分配。

## 3. 参数模型（Options）

`PidControllerOptions` 建议字段：

- `decimal Kp`：比例系数。
- `decimal Ki`：积分系数。
- `decimal Kd`：微分系数。
- `decimal SamplePeriodSeconds`：采样周期（秒），必须 `> 0`。
- `decimal OutputMinHz`：输出下限（Hz）。
- `decimal OutputMaxHz`：输出上限（Hz），必须 `>= OutputMinHz`。
- `decimal IntegralMin`：积分项下限。
- `decimal IntegralMax`：积分项上限，必须 `>= IntegralMin`。
- `decimal DerivativeFilterAlpha`（可选）：微分一阶低通滤波系数，建议范围 `[0,1]`。

校验策略：

- 构造或 `Validate` 时进行参数合法性校验，不合法抛 `ArgumentOutOfRangeException`。
- 默认值建议与现有 `LoopTrackPidOptions` 一致（`Kp=1, Ki=0, Kd=0`），其余字段给出保守默认范围。

## 4. 输入/输出与状态

### 4.1 输入（PidControllerInput）

- `decimal TargetHz`：目标速度对应频率（Hz）。
- `decimal ActualHz`：当前反馈频率（Hz）。
- `bool FreezeIntegral`（可选）：外部触发积分冻结（例如启动阶段）。

### 4.2 状态（PidControllerState）

- `decimal Integral`：积分累计值。
- `decimal LastError`：上一次偏差。
- `decimal LastDerivative`：上一次微分值（用于滤波）。
- `bool IsInitialized`：是否完成首帧初始化。

### 4.3 输出（PidControllerOutput）

- `decimal CommandHz`：本次建议写入变频器的频率给定（Hz）。
- `decimal Error`：当前偏差（`TargetHz - ActualHz`）。
- `decimal Proportional`：比例项贡献。
- `decimal Integral`：积分项贡献。
- `decimal Derivative`：微分项贡献。
- `decimal UnclampedHz`：限幅前输出。
- `bool IsOutputClamped`：是否触发输出限幅。
- `PidControllerState NextState`：下一状态。

## 5. 计算流程（离散 PID）

建议公开方法：

- `public PidControllerOutput Compute(in PidControllerInput input, in PidControllerState state)`

计算步骤：

1. 计算误差：`error = TargetHz - ActualHz`。
2. 比例项：`p = Kp * error`。
3. 积分项更新：
   - 若 `FreezeIntegral=true`，保持积分不变；
   - 否则 `integralCandidate = state.Integral + error * SamplePeriodSeconds`；
   - 对积分累计执行 `[IntegralMin, IntegralMax]` 限幅。
4. 微分项：
   - 首帧（`IsInitialized=false`）可令微分为 `0`，避免启动尖峰；
   - 否则 `rawDerivative = (error - state.LastError) / SamplePeriodSeconds`；
   - 若启用滤波：`derivative = alpha * rawDerivative + (1 - alpha) * state.LastDerivative`。
5. 三项合成：
   - `i = Ki * integral`
   - `d = Kd * derivative`
   - `unclamped = TargetHz + p + i + d`（建议以目标频率为前馈基线）。
6. 输出限幅：
   - `command = clamp(unclamped, OutputMinHz, OutputMaxHz)`。
7. 防积分饱和（Anti-windup）：
   - 当输出已到上下限且误差仍推动同方向饱和时，回退本次积分更新（或采用条件积分策略）。
8. 生成 `NextState` 并返回结果。

## 6. 异常与性能约束

- 纯计算路径不做日志输出，异常由调用方捕获并按全局规则记录日志。
- 不分配临时集合、不使用反射，确保高频调用性能。
- 所有计算不引入 UTC/时间戳逻辑，仅依赖传入采样周期参数。

## 7. 与现有模块的接入关系

- 参数来源：可由 `LoopTrackPidOptions` 映射到 `PidControllerOptions`（Kp/Ki/Kd）。
- 调用位置：建议在执行层稳速调度循环中调用 `Compute`，将 `CommandHz` 写入驱动层（例如写 `P0.07/F007H`）。
- 事件/告警：
  - 限幅命中可复用现有轨道事件（如频率限幅事件）；
  - 纯计算器不直接发布事件，由上层根据输出决定是否发布。

## 8. 单元测试规划（实现阶段）

建议测试用例：

1. `SamplePeriodSeconds <= 0` 时参数校验抛异常。
2. `OutputMinHz > OutputMaxHz` 时参数校验抛异常。
3. 首帧计算微分项为 0，不出现启动尖峰。
4. 输出超过上/下限时正确限幅。
5. 限幅+同向误差场景触发防积分饱和。
6. `FreezeIntegral=true` 时积分保持不变。
7. 固定输入下输出可收敛至目标附近（基础稳定性回归）。

## 9. 实施顺序建议

1. 先落地 `PidControllerOptions/Input/State/Output` 数据结构与校验。
2. 实现 `PidController.Compute` 与 anti-windup。
3. 补齐单元测试，验证边界与收敛行为。
4. 在执行层做最小侵入接入，并复用现有事件模型进行可观测性补充。
