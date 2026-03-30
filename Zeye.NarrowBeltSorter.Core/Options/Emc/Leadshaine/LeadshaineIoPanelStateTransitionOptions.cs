using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine {

    /// <summary>
    /// IoPanel 按钮到系统状态流转配置。
    /// </summary>
    public sealed class LeadshaineIoPanelStateTransitionOptions {

        /// <summary>
        /// 启动预警时长（单位：毫秒）。
        /// </summary>
        public int StartupWarningDurationMs { get; set; } = 3000;
    }
}
