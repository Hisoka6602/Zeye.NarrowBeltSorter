using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Zeye.NarrowBeltSorter.Core.Manager.Carrier {

    /// <summary>
    /// 小车接口（描述单台小车状态与控制能力）
    /// </summary>
    public interface ICarrier : IDisposable {
        //----------属性-----------
        //小车Id(long)
        //小车当前运行速度
        //小车当前转向(左右)
        //小车当前连接状态
        //小车当前载货状态
        //小车包裹信息(可空)
        //装载并联小车Id集合
        //是否被装载并联
        //----------事件-----------
        //小车载货状态变更事件
        //小车连接状态变更事件
        //----------方法-----------
        //连接小车
        //断开小车连接
        //设置小车转向方向
        //设置小车运行速度
        //装载包裹(设置包裹信息,并联小车Id集合)
        //卸载包裹(移除包裹信息,移除并联小车Id集合)
    }
}
