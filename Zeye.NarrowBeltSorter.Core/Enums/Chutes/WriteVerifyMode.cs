using System.ComponentModel;

namespace Zeye.NarrowBeltSorter.Core.Enums.Chutes {

    /// <summary>
    /// 智嵌 DO 写后读校验策略模式。
    /// </summary>
    public enum WriteVerifyMode {

        /// <summary>
        /// 仅告警，不中断当前流程。
        /// </summary>
        [Description("仅告警")]
        WarnOnly = 0,

        /// <summary>
        /// 首次校验失败后重试一次写入与回读，仍失败则中断并置故障。
        /// </summary>
        [Description("重试后失败")]
        RetryThenFail = 1
    }
}
