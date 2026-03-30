namespace Zeye.NarrowBeltSorter.Core.Options.SignalTower {

    using System;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Collections.Generic;

    using System;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Collections.Generic;

    namespace Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine {
        /// <summary>
        /// Leadshaine 信号塔配置。
        /// </summary>
        public sealed record LeadshaineSignalTowerOptions {
            /// <summary>
            /// 是否启用信号塔。
            /// </summary>
            public bool Enabled { get; set; }

            /// <summary>
            /// 信号塔 Id。
            /// </summary>
            public long Id { get; set; } = 1;

            /// <summary>
            /// 信号塔名称。
            /// </summary>
            public string Name { get; set; } = "EmcSignalTower";

            /// <summary>
            /// 红灯输出点位 Id（引用 Leadshaine:PointBindings:Points[*].PointId）。
            /// </summary>
            public string RedLightPointId { get; set; } = string.Empty;

            /// <summary>
            /// 黄灯输出点位 Id（引用 Leadshaine:PointBindings:Points[*].PointId）。
            /// </summary>
            public string YellowLightPointId { get; set; } = string.Empty;

            /// <summary>
            /// 绿灯输出点位 Id（引用 Leadshaine:PointBindings:Points[*].PointId）。
            /// </summary>
            public string GreenLightPointId { get; set; } = string.Empty;

            /// <summary>
            /// 蜂鸣器输出点位 Id（引用 Leadshaine:PointBindings:Points[*].PointId）。
            /// </summary>
            public string BuzzerPointId { get; set; } = string.Empty;
        }
    }
}
