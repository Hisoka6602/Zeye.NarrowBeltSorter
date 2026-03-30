using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Zeye.NarrowBeltSorter.Core.Options.Carrier {

    /// <summary>
    /// 红外感应器小车管理配置。
    /// </summary>
    public sealed class CarrierManagerOptions {

        /// <summary>
        /// 上车点相对感应位小车偏移。
        /// </summary>
        public int LoadingZoneCarrierOffset { get; set; }

        /// <summary>
        /// 格口相对感应位小车偏移映射（键：格口 Id；值：偏移小车数量）。
        /// </summary>
        public Dictionary<long, int> ChuteCarrierOffsetMap { get; set; } = new();
    }
}
