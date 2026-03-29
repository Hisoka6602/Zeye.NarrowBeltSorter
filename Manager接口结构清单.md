# Manager接口结构清单

## 维护约束

- 本文档按 `Zeye.NarrowBeltSorter.Core/Manager` 目录维护接口树状图。
- 每个接口节点需同步维护三类信息：接口文件、实现文件、使用类文件（含 Host 与关键测试桩）。
- 后续新增、删除或重命名 `Manager` 目录下接口文件（`I*.cs`）及其实现/关键使用文件时，必须同步更新本文档。

## Manager 目录接口关系树状图

```text
Zeye.NarrowBeltSorter.Core/Manager
├── Carrier
│   ├── ICarrier.cs                                   # 载具实体抽象
│   │   ├── 实现文件
│   │   │   └── （暂无实现）
│   │   └── 使用类文件
│   │       └── ICarrierManager.cs
│   └── ICarrierManager.cs                            # 载具管理器抽象
│       ├── 实现文件
│       │   └── （暂无实现）
│       └── 使用类文件
│           └── （暂无关键引用）
├── Chutes
│   ├── IChute.cs                                     # 格口实体抽象
│   │   ├── 实现文件
│   │   │   └── Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianChute.cs
│   │   └── 使用类文件
│   │       ├── Zeye.NarrowBeltSorter.Core/Manager/Chutes/IChuteManager.cs
│   │       └── Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianChuteManager.cs
│   ├── IChuteManager.cs                              # 格口管理器抽象
│   │   ├── 实现文件
│   │   │   └── Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianChuteManager.cs
│   │   └── 使用类文件
│   │       ├── Zeye.NarrowBeltSorter.Host/Program.cs
│   │       ├── Zeye.NarrowBeltSorter.Host/Services/ChuteSelfHandlingHostedService.cs
│   │       └── Zeye.NarrowBeltSorter.Host/Services/ChuteForcedRotationService.cs
│   └── IZhiQianClientAdapter.cs                      # 智嵌格口客户端适配器抽象
│       ├── 实现文件
│       │   ├── Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianBinaryClientAdapter.cs
│       │   └── Zeye.NarrowBeltSorter.Core.Tests/FakeZhiQianClientAdapter.cs
│       └── 使用类文件
│           ├── Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianChuteManager.cs
│           ├── Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianChute.cs
│           └── Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianClientAdapterFactory.cs
├── InductionLane
│   ├── IInductionLane.cs                             # 供包位实体抽象
│   │   ├── 实现文件
│   │   │   └── （暂无实现）
│   │   └── 使用类文件
│   │       └── （暂无关键引用）
│   └── IInductionLaneManager.cs                      # 供包位管理器抽象
│       ├── 实现文件
│       │   └── （暂无实现）
│       └── 使用类文件
│           └── （暂无关键引用）
├── Parcel
│   └── IParcelManager.cs                             # 包裹管理器抽象
│       ├── 实现文件
│       │   └── （暂无实现）
│       └── 使用类文件
│           └── （暂无关键引用）
├── Protocols
│   └── IInfraredDriverFrameCodec.cs                  # 红外驱动协议帧编解码抽象
│       ├── 实现文件
│       │   └── Zeye.NarrowBeltSorter.Drivers/Vendors/Leadshaine/Infrared/LeadshaineInfraredDriverFrameCodec.cs
│       └── 使用类文件
│           ├── Zeye.NarrowBeltSorter.Host/Program.cs
│           ├── Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianChuteManager.cs
│           └── Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianChute.cs
├── Realtime
│   └── IDeviceRealtimePublisher.cs                   # 设备实时数据发布抽象
│       ├── 实现文件
│       │   └── （暂无实现）
│       └── 使用类文件
│           └── （暂无关键引用）
├── Sensor
│   └── ISensorManager.cs                             # 传感器管理器抽象
│       ├── 实现文件
│       │   └── （暂无实现）
│       └── 使用类文件
│           └── （暂无关键引用）
├── Signal
│   └── ISignalTower.cs                               # 信号塔抽象
│       ├── 实现文件
│       │   └── （暂无实现）
│       └── 使用类文件
│           └── （暂无关键引用）
├── SortTask
│   └── ISortTaskManager.cs                           # 分拣任务管理器抽象
│       ├── 实现文件
│       │   └── （暂无实现）
│       └── 使用类文件
│           └── （暂无关键引用）
├── System
│   └── ISystemStateManager.cs                        # 系统状态管理器抽象
│       ├── 实现文件
│       │   └── （暂无实现）
│       └── 使用类文件
│           └── （暂无关键引用）
└── TrackSegment
    ├── ILeiMaModbusClientAdapter.cs                  # 雷码 Modbus 客户端适配器抽象
    │   ├── 实现文件
    │   │   ├── Zeye.NarrowBeltSorter.Drivers/Vendors/LeiMa/LeiMaModbusClientAdapter.cs
    │   │   └── Zeye.NarrowBeltSorter.Core.Tests/FakeLeiMaModbusClientAdapter.cs
    │   └── 使用类文件
    │       ├── Zeye.NarrowBeltSorter.Drivers/Vendors/LeiMa/LeiMaLoopTrackManager.cs
    │       └── Zeye.NarrowBeltSorter.Host/Services/LoopTrackManagerService.cs
    └── ILoopTrackManager.cs                          # 环道管理器抽象
        ├── 实现文件
        │   ├── Zeye.NarrowBeltSorter.Drivers/Vendors/LeiMa/LeiMaLoopTrackManager.cs
        │   └── Zeye.NarrowBeltSorter.Core.Tests/FakeLoopTrackManager.cs
        └── 使用类文件
            ├── Zeye.NarrowBeltSorter.Host/Services/LoopTrackManagerService.cs
            └── Zeye.NarrowBeltSorter.Host/Services/LoopTrackHILWorker.cs
```
