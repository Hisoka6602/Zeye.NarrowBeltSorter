using Zeye.NarrowBeltSorter.Core.Enums.Io;

namespace Zeye.NarrowBeltSorter.Core.Models.Emc {
    /// <summary>
    /// EMC 监控 IO 点位快照。
    /// </summary>
    public sealed class IoPointInfo {

        /// <summary>
        /// 点位编号（业务逻辑点位编号）。
        /// </summary>
        public required int Point { get; init; }

        /// <summary>
        /// 点位名称（用于日志与诊断）。
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// 点位类型（输入/输出分类）。
        /// </summary>
        public required IoPointType Type { get; init; }

        /// <summary>
        /// 当前电平状态。
        /// </summary>
        public IoState State { get; set; }
    }
}
