using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Zeye.NarrowBeltSorter.Core.Manager.InductionLane {

    /// <summary>
    /// 供包台
    /// </summary>
    public interface IInductionLane {
        //-----------字段--------------
        //供包台Id
        //供包台名称
        //当前连接状态
        //创建包裹到上车位距离（mm）
        //皮带速度
        //是否首次稳速后再启动
        //供包台皮带IO(集合)
        //供包台状态变化
        //创建包裹Io(SensorInfo类型)
        //是否监控包裹长度
        //-----------事件--------------
        //包裹创建事件
        //包裹到达上车位事件
        //供包台状态变化事件
        //-----------方法--------------
        //设置供包台配置
        //启动供包台
        //停止供包台
    }
}
