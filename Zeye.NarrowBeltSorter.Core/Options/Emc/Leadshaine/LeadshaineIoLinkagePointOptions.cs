using Zeye.NarrowBeltSorter.Core.Enums.System;

namespace Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine {
    /// <summary>
    /// Leadshaine 联动 IO 单点配置。
    /// </summary>
    public sealed class LeadshaineIoLinkagePointOptions {
        /// <summary>
        /// 触发联动的系统状态。
        /// </summary>
        public SystemState RelatedSystemState { get; set; }

        /// <summary>
        /// 目标输出点位标识（需在 PointBindings 中存在且为 Output）。
        /// </summary>
        public string PointId { get; set; } = string.Empty;

        /// <summary>
        /// 触发时写入值（true 表示高电平，false 表示低电平）。
        /// </summary>
        public bool TriggerValue { get; set; } = true;

        /// <summary>
        /// 执行延迟（单位：毫秒，最小值为 0）。
        /// </summary>
        public int DelayMs { get; set; }

        /// <summary>
        /// 保持时长（单位：毫秒；小于等于 0 表示只触发不回写）。
        /// </summary>
        public int DurationMs { get; set; }
    }
}
