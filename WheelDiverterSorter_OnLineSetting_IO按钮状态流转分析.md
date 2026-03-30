# WheelDiverterSorter（OnLine-Setting）IO 按钮改变系统状态流程分析

> 目标仓库：`https://github.com/Hisoka6602/WheelDiverterSorter/tree/OnLine-Setting`

## 1. 总体结论

在 `OnLine-Setting` 分支中，**系统状态的改变由 IoPanel 驱动层直接调用 `ISystemStateManager.ChangeStateAsync(...)` 完成**，不是由 Host 再二次转发。

- `Start` 按钮按下：切到 `Running`
- `Stop` 按钮按下：切到 `Paused`
- `Reset` 按钮按下：切到 `Booting`
- `EmergencyStop` 按下：切到 `EmergencyStop`
- `EmergencyStop` 释放且全部急停点释放：切到 `Ready`

## 2. 关键路径

### 2.1 采样与边沿检测

`SiemensS7IoPanel` / `LeadshaineIoPanel` 监控循环从 EMC 快照中读取点位状态，按 `TriggerState` 判断按下/释放边沿，并做去抖。

- 首次采样只记录，不触发事件（避免上电瞬间误触发）
- 急停有“锁存位”避免重复切换

### 2.2 按钮 -> 状态映射

非急停按钮通过 `HandleButtonPressedAsync(...)` 完成状态切换：

- `Start` -> `Running`（前置可执行预警 IO）
- `Stop` -> `Paused`
- `Reset` -> `Booting`

急停按钮：

- 按下边沿：`EmergencyStop`
- 释放边沿：当全部急停点都释放后切 `Ready`

### 2.3 状态机约束

`SystemStateManager` 会校验状态流转合法性：

- 当前 `EmergencyStop`：只允许到 `Ready`
- 当前 `Faulted`：只允许到 `Booting`
- 其他状态：允许任意转换

因此，按钮调用 `ChangeStateAsync` 后，最终是否成功仍受状态机规则约束。

## 3. 与“只监控到单点按钮”的关系

`IoMonitoringHostedService` 启动时先以 IoPanel 按钮点构建并下发监控列表，再启动 Sensor；Sensor 点通常在其启动阶段再同步到 EMC。

所以系统启动早期确实可能出现“先看到按钮点位已监控，传感器点位稍后补齐”的观察结果。

## 4. 排查建议

1. 打开 IoPanel 监控启动日志，确认按钮点位是否已进入快照。
2. 打开 Sensor 启动日志，确认 Sensor 是否成功执行同步点位。
3. 观察 `SystemStateManager` 拒绝流转日志（非法状态流转会被拒绝而返回 false）。
4. 急停场景下优先检查“是否所有急停点都释放”，否则不会回到 `Ready`。
