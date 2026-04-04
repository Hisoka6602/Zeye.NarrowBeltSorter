namespace Zeye.NarrowBeltSorter.Core.Options.Chutes {

    /// <summary>
    /// 格口强排后台服务配置。
    /// 支持两种互斥模式：轮转模式（<see cref="ChuteSequence"/> 非空时生效，优先级更高）
    /// 与固定模式（<see cref="FixedChuteId"/> 有值且 <see cref="ChuteSequence"/> 为空时生效）。
    /// </summary>
    public sealed record ChuteForcedRotationOptions {

        /// <summary>
        /// 是否启用格口强排后台服务。
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 强排切换间隔（单位：秒，最小值 1，默认 10）。仅轮转模式下有效。
        /// </summary>
        public int SwitchIntervalSeconds { get; set; } = 10;

        /// <summary>
        /// 轮转强排格口数组（按顺序循环切换）。非空时启用轮转模式，忽略 <see cref="FixedChuteId"/>。
        /// </summary>
        public List<long> ChuteSequence { get; set; } = new();

        /// <summary>
        /// 固定强排格口 Id。仅在 <see cref="ChuteSequence"/> 为空时生效。
        /// 系统处于 Running 状态时闭合该格口，离开 Running 时自动断开。
        /// null 表示不启用 Running 固定模式。
        /// </summary>
        public long? FixedChuteId { get; set; }

        /// <summary>
        /// 检修状态强排格口集合（按配置顺序轮转）。
        /// 仅在系统处于 Maintenance 状态时生效；数组元素需为正整数格口 Id。
        /// 为空或全为非法值时，检修状态不执行强排并主动断开强排。
        /// </summary>
        public List<long> MaintenanceChuteSequence { get; set; } = new();
    }
}
