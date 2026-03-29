using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Zeye.NarrowBeltSorter.Core.Manager.Signal {

    /// <summary>
    /// 信号塔接口，定义了信号塔的基本功能和行为。
    /// </summary>
    public interface ISignalTower {
        //------------字段------------

        //红灯IO(SensorInfo)
        //绿灯IO(SensorInfo)
        //蜂鸣器IO(SensorInfo)
        //黄灯IO(SensorInfo)

        //当前三色灯状态
        //当前蜂鸣器状态
        //当前连接状态

        //------------事件------------
        // 三色灯状态变更事件
        // 蜂鸣器状态变更事件
        // 连接状态变更事件

        //------------方法------------

        //设置三色灯状态
        //设置蜂鸣器状态
    }
}
