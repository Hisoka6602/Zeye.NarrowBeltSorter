using Zeye.NarrowBeltSorter.Core.Enums.Io;

namespace Zeye.NarrowBeltSorter.Core.Models.Sensor {

    /// <summary>
    /// 传感器点位信息，描述单个 IO 点的编号、类型与当前电平状态。
    /// </summary>
    public class SensorInfo {

        /// <summary>
        /// 点位编号
        /// </summary>
        public required int Point { get; init; }

        /// <summary>
        /// 点位类型
        /// </summary>
        public required IoPointType Type { get; init; }

        /// <summary>
        /// 当前 IO 电平状态（High 表示高电平，Low 表示低电平）
        /// </summary>
        public IoState State { get; set; }
    }
}
