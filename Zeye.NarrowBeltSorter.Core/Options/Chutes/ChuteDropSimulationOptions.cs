namespace Zeye.NarrowBeltSorter.Core.Options.Chutes {

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

    using System;
    using System;
    using System.Linq;
    using System.Text;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Threading.Tasks;
    using System.Collections.Generic;
    using System.Collections.Generic;

    using System;
    using System;
    using System;
    using System;
    using System.Linq;
    using System.Text;
    using System.Linq;
    using System.Text;
    using System.Linq;
    using System.Text;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Threading.Tasks;
    using System.Threading.Tasks;
    using System.Threading.Tasks;
    using System.Collections.Generic;
    using System.Collections.Generic;
    using System.Collections.Generic;
    using System.Collections.Generic;

    using System;
    using System;
    using System;
    using System;
    using System;
    using System;
    using System;
    using System;
    using System.Linq;
    using System.Text;
    using System.Linq;
    using System.Text;
    using System.Linq;
    using System.Text;
    using System.Linq;
    using System.Text;
    using System.Linq;
    using System.Text;
    using System.Linq;
    using System.Text;
    using System.Linq;
    using System.Text;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Threading.Tasks;
    using System.Threading.Tasks;
    using System.Threading.Tasks;
    using System.Threading.Tasks;
    using System.Threading.Tasks;
    using System.Threading.Tasks;
    using System.Threading.Tasks;
    using System.Collections.Generic;
    using System.Collections.Generic;
    using System.Collections.Generic;
    using System.Collections.Generic;
    using System.Collections.Generic;
    using System.Collections.Generic;
    using System.Collections.Generic;
    using System.Collections.Generic;

    namespace Zeye.NarrowBeltSorter.Core.Options.Chutes {
        /// <summary>
        /// 包裹落格模拟托管服务配置。
        /// </summary>
        public sealed record ChuteDropSimulationOptions {
            /// <summary>
            /// 是否启用包裹落格模拟托管服务。
            /// </summary>
            public bool Enabled { get; init; }

            /// <summary>
            /// 模拟分配模式（Fixed/RoundRobin）。
            /// </summary>
            public string Mode { get; init; } = "Fixed";

            /// <summary>
            /// 固定模式目标格口 Id（Mode=Fixed 时生效）。
            /// </summary>
            public long FixedChuteId { get; init; }

            /// <summary>
            /// 轮询模式格口序列（Mode=RoundRobin 时生效）。
            /// </summary>
            public IReadOnlyList<long> ChuteSequence { get; init; } = [];

            /// <summary>
            /// 创建包裹后延迟分配格口时间（毫秒）。
            /// </summary>
            public int AssignDelayMs { get; init; } = 100;
        }
    }
}
