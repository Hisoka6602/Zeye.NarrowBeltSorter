using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Zeye.NarrowBeltSorter.Core.Manager.Carrier {

    /// <summary>
    /// 小车管理器（负责小车集合初始化、连接与调度控制）
    /// </summary>
    public interface ICarrierManager : IAsyncDisposable {
        //----------属性-----------
        //当前小车集合
        //小车建环是否已完成
        //小车与格口相对位置配置（当1号小车被感应到时,每个格口上对应的小车Id）
        //当前落格配置方案 -DropMode
        //当前感应位置小车Id
        //当前载货小车集合
        //当前载货区位置小车Id
        //----------事件-----------
        //小车建环完成事件
        //当前感应位置小车变更事件
        //载货小车进入格口(感应区)事件
        //小车载货状态变更事件
        //小车连接状态变更事件
        //----------方法-----------
    }
}
