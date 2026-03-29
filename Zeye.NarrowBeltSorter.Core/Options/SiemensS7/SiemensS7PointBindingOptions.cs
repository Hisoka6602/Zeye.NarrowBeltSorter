using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.SiemensS7;

namespace Zeye.NarrowBeltSorter.Core.Options.SiemensS7 {
    /// <summary>
    /// 西门子 S7 逻辑点位绑定配置。
    /// </summary>
    public sealed record SiemensS7PointBindingOptions {

        /// <summary>
        /// 逻辑点位编号。
        /// </summary>
        public int PointId { get; set; }

        /// <summary>
        /// 逻辑点位名称。
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 逻辑点位类型。
        /// </summary>
        public IoPointType PointType { get; set; }

        /// <summary>
        /// 点位触发电平。
        /// </summary>
        public IoState TriggerState { get; set; } = IoState.High;

        /// <summary>
        /// S7 地址区（I/Q/DB）。
        /// </summary>
        public SiemensS7AddressArea Area { get; set; } = SiemensS7AddressArea.Input;

        /// <summary>
        /// DB 号（Area=DataBlock 时生效）。
        /// </summary>
        public int DbNumber { get; set; }

        /// <summary>
        /// 字节偏移。
        /// </summary>
        public int ByteOffset { get; set; }

        /// <summary>
        /// 位索引（0~7）。
        /// </summary>
        public int BitIndex { get; set; }
    }
}
