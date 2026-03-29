namespace Zeye.NarrowBeltSorter.Core.Models.Emc {
    /// <summary>
    /// EMC IO 点位快照信息。
    /// </summary>
    public readonly record struct IoPointInfo {
        /// <summary>
        /// 逻辑点位标识。
        /// </summary>
        public required string PointId { get; init; }

        /// <summary>
        /// 点位区域（Input/Output）。
        /// </summary>
        public required string Area { get; init; }

        /// <summary>
        /// 板卡序号。
        /// </summary>
        public required ushort CardNo { get; init; }

        /// <summary>
        /// 端口序号。
        /// </summary>
        public required ushort PortNo { get; init; }

        /// <summary>
        /// 位索引。
        /// </summary>
        public required int BitIndex { get; init; }

        /// <summary>
        /// 点位当前值。
        /// </summary>
        public required bool Value { get; init; }

        /// <summary>
        /// 快照时间（本地时间语义）。
        /// </summary>
        public required DateTime CapturedAt { get; init; }
    }
}
