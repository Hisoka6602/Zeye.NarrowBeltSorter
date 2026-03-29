# Manager接口结构清单

## 维护约束

- 本文档按 `Zeye.NarrowBeltSorter.Core/Manager` 目录下接口分层维护树状结构。
- 后续新增、删除或重命名 `Manager` 目录下接口文件（`I*.cs`）时，必须同步更新本文档。

## 接口树状图

```text
Zeye.NarrowBeltSorter.Core/Manager
├── Carrier
│   ├── ICarrier.cs                        # 载具实体抽象
│   └── ICarrierManager.cs                 # 载具管理器抽象
├── Chutes
│   ├── IChute.cs                          # 格口实体抽象
│   ├── IChuteManager.cs                   # 格口管理器抽象
│   └── IZhiQianClientAdapter.cs           # 智嵌格口客户端适配器抽象
├── InductionLane
│   ├── IInductionLane.cs                  # 供包位实体抽象
│   └── IInductionLaneManager.cs           # 供包位管理器抽象
├── Parcel
│   └── IParcelManager.cs                  # 包裹管理器抽象
├── Protocols
│   └── IInfraredDriverFrameCodec.cs       # 红外驱动协议帧编解码抽象
├── Realtime
│   └── IDeviceRealtimePublisher.cs        # 设备实时数据发布抽象
├── Sensor
│   └── ISensorManager.cs                  # 传感器管理器抽象
├── Signal
│   └── ISignalTower.cs                    # 信号塔抽象
├── SortTask
│   └── ISortTaskManager.cs                # 分拣任务管理器抽象
├── System
│   └── ISystemStateManager.cs             # 系统状态管理器抽象
└── TrackSegment
    ├── ILeiMaModbusClientAdapter.cs       # 雷码 Modbus 客户端适配器抽象
    └── ILoopTrackManager.cs               # 环道管理器抽象
```
