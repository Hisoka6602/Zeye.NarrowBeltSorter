namespace Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine {

    /// <summary>
    /// Leadshaine 信号塔配置。
    /// </summary>
    public sealed record LeadshaineSignalTowerOptions {
        /// <summary>
        /// 是否启用信号塔（取值：true/false）。
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 信号塔 Id（最小值：1）。
        /// </summary>
        public long Id { get; set; } = 1;

        /// <summary>
        /// 信号塔名称（不能为空）。
        /// </summary>
        public string Name { get; set; } = "EmcSignalTower";

        /// <summary>
        /// 红灯输出点位 Id（引用 Leadshaine:PointBindings:Points[*].PointId；配置为 "0" 表示弃用红灯）。
        /// </summary>
        public string RedLightPointId { get; set; } = string.Empty;

        /// <summary>
        /// 黄灯输出点位 Id（引用 Leadshaine:PointBindings:Points[*].PointId；配置为 "0" 表示弃用黄灯）。
        /// </summary>
        public string YellowLightPointId { get; set; } = string.Empty;

        /// <summary>
        /// 绿灯输出点位 Id（引用 Leadshaine:PointBindings:Points[*].PointId；配置为 "0" 表示弃用绿灯）。
        /// </summary>
        public string GreenLightPointId { get; set; } = string.Empty;

        /// <summary>
        /// 蜂鸣器输出点位 Id（引用 Leadshaine:PointBindings:Points[*].PointId；配置为 "0" 表示弃用蜂鸣器）。
        /// </summary>
        public string BuzzerPointId { get; set; } = string.Empty;
    }
}
