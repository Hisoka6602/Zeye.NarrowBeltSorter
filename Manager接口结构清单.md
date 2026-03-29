# Manager接口结构清单

## 维护约束

- 本文档按 `Zeye.NarrowBeltSorter.Core/Manager` 目录维护接口树状图。
- 每个接口节点需同步维护三类信息：接口文件、实现文件、使用类文件（含 Host 与关键测试桩）。
- 后续新增、删除或重命名 `Manager` 目录下接口文件（`I*.cs`）及其实现/关键使用文件时，必须同步更新本文档。

## Manager 目录接口关系树状图

```text
Zeye.NarrowBeltSorter.Core/Manager                        # Manager 接口分层根目录
├── Emc
│   ├── IEmcController.cs                             # EMC 控制器抽象（初始化/监控/写入/重连）
│   │   ├── 实现文件
│   │   │   └── Zeye.NarrowBeltSorter.Drivers/Vendors/Leadshaine/Emc/LeadshaineEmcController.cs  # 雷赛 EMC 控制器实现
│   │   └── 使用类文件
│   │       └── Zeye.NarrowBeltSorter.Host/Vendors/DependencyInjection/HostApplicationBuilderLeadshaineExtensions.cs  # EMC 控制器依赖注入注册
│   └── IEmcHardwareAdapter.cs                        # EMC 硬件访问抽象（参数化初始化）
│       ├── 实现文件
│       │   ├── Zeye.NarrowBeltSorter.Drivers/Vendors/Leadshaine/Emc/LeadshaineEmcHardwareAdapter.cs  # 雷赛 EMC 硬件访问实现
│       │   └── Zeye.NarrowBeltSorter.Core.Tests/Leadshaine/Emc/FakeLeadshaineEmcHardwareAdapter.cs   # EMC 硬件访问测试桩实现
│       └── 使用类文件
│           └── Zeye.NarrowBeltSorter.Drivers/Vendors/Leadshaine/Emc/LeadshaineEmcController.cs       # EMC 控制器通过适配器访问底层硬件
├── Carrier
│   ├── ICarrier.cs                                   # 载具实体抽象
│   │   ├── 实现文件
│   │   │   └── （暂无实现）
│   │   └── 使用类文件
│   │       └── ICarrierManager.cs                         # 载具管理器接口（用于载具集合与查询）
│   └── ICarrierManager.cs                            # 载具管理器抽象
│       ├── 实现文件
│       │   └── （暂无实现）
│       └── 使用类文件
│           └── （暂无关键引用）
├── Chutes
│   ├── IChute.cs                                     # 格口实体抽象
│   │   ├── 实现文件
│   │   │   └── Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianChute.cs  # 智嵌格口实体实现
│   │   └── 使用类文件
│   │       ├── Zeye.NarrowBeltSorter.Core/Manager/Chutes/IChuteManager.cs      # 格口管理器接口（暴露格口集合）
│   │       └── Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianChuteManager.cs  # 智嵌格口管理器（聚合格口实体）
│   ├── IChuteManager.cs                              # 格口管理器抽象
│   │   ├── 实现文件
│   │   │   └── Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianChuteManager.cs  # 智嵌格口管理器实现
│   │   └── 使用类文件
│   │       ├── Zeye.NarrowBeltSorter.Host/Program.cs                             # 依赖注入注册入口
│   │       ├── Zeye.NarrowBeltSorter.Host/Services/ChuteSelfHandlingHostedService.cs  # 格口自处理托管服务
│   │       └── Zeye.NarrowBeltSorter.Host/Services/ChuteForcedRotationService.cs  # 格口强制轮转托管服务
│   └── IZhiQianClientAdapter.cs                      # 智嵌格口客户端适配器抽象
│       ├── 实现文件
│       │   ├── Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianBinaryClientAdapter.cs  # 智嵌二进制客户端实现
│       │   └── Zeye.NarrowBeltSorter.Core.Tests/FakeZhiQianClientAdapter.cs      # 智嵌客户端测试桩实现
│       └── 使用类文件
│           ├── Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianChuteManager.cs  # 管理器依赖适配器读写
│           ├── Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianChute.cs     # 格口实体依赖适配器执行命令
│           └── Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianClientAdapterFactory.cs  # 适配器工厂返回接口实例
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
│       │   └── Zeye.NarrowBeltSorter.Drivers/Vendors/Leadshaine/Infrared/LeadshaineInfraredDriverFrameCodec.cs  # 雷赛红外协议编解码实现
│       └── 使用类文件
│           ├── Zeye.NarrowBeltSorter.Host/Program.cs                              # 依赖注入注册入口
│           ├── Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianChuteManager.cs  # 管理器调用编码能力
│           └── Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianChute.cs      # 格口实体调用编码能力
├── Realtime
│   └── IDeviceRealtimePublisher.cs                   # 设备实时数据发布抽象
│       ├── 实现文件
│       │   └── （暂无实现）
│       └── 使用类文件
│           └── （暂无关键引用）
├── Sensor
│   └── ISensorManager.cs                             # 传感器管理器抽象
│       ├── 实现文件
│       │   └── Zeye.NarrowBeltSorter.Drivers/Vendors/Leadshaine/Sensor/LeadshaineSensorManager.cs  # 雷赛传感器管理器实现
│       └── 使用类文件
│           └── Zeye.NarrowBeltSorter.Host/Services/Hosted/IoMonitoringHostedService.cs  # Io 监控托管服务编排传感器启停
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
    │   │   ├── Zeye.NarrowBeltSorter.Drivers/Vendors/LeiMa/LeiMaModbusClientAdapter.cs  # 雷码Modbus客户端实现
    │   │   └── Zeye.NarrowBeltSorter.Core.Tests/FakeLeiMaModbusClientAdapter.cs  # 雷码客户端测试桩实现
    │   └── 使用类文件
    │       ├── Zeye.NarrowBeltSorter.Drivers/Vendors/LeiMa/LeiMaLoopTrackManager.cs  # 环道管理器依赖适配器通信
    │       └── Zeye.NarrowBeltSorter.Host/Services/LoopTrackManagerService.cs     # 托管服务创建适配器实例
    └── ILoopTrackManager.cs                          # 环道管理器抽象
        ├── 实现文件
        │   ├── Zeye.NarrowBeltSorter.Drivers/Vendors/LeiMa/LeiMaLoopTrackManager.cs  # 雷码环道管理器实现
        │   └── Zeye.NarrowBeltSorter.Core.Tests/FakeLoopTrackManager.cs           # 环道管理器测试桩实现
        └── 使用类文件
            ├── Zeye.NarrowBeltSorter.Host/Services/LoopTrackManagerService.cs      # 环道托管服务主流程
            └── Zeye.NarrowBeltSorter.Host/Services/LoopTrackHILWorker.cs           # HIL 环道托管流程
```
