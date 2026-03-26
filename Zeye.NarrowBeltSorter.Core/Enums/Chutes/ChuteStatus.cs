using System;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Zeye.NarrowBeltSorter.Core.Enums.Chutes {

    /// <summary>
    /// 格口状态枚举。
    /// </summary>
    public enum ChuteStatus {

        /// <summary>
        /// 空闲。
        /// </summary>
        [Description("空闲")]
        Idle = 1,

        /// <summary>
        /// 锁格。
        /// </summary>
        [Description("锁格")]
        Locked = 2,

        /// <summary>
        /// 满格。
        /// </summary>
        [Description("满格")]
        Full = 3,

        /// <summary>
        /// 故障。
        /// </summary>
        [Description("故障")]
        Faulted = 4
    }
}
