using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.System;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    public class ChuteDropSimulationHostedService : BackgroundService {
        private readonly ILogger<ChuteDropSimulationHostedService> _logger;
        private readonly IParcelManager _parcelManager;
        private readonly ISystemStateManager _systemStateManager;

        public ChuteDropSimulationHostedService(
            ILogger<ChuteDropSimulationHostedService> logger,
            IParcelManager parcelManager,
            ISystemStateManager systemStateManager
           ) {
            _logger = logger;
            _parcelManager = parcelManager;
            _systemStateManager = systemStateManager;

            _parcelManager.ParcelCreated += (sender, args) => {
                if (_systemStateManager.CurrentState == SystemState.Running) {
                    //可在appsettings.Development.json、appsettings.json中配置模式(固定格口、轮询格口,轮询格口需要能配置数组，类似轮询强排)

                    //等待一定时间后给包裹格口赋值,等待的时间可以在配置文件中配置,类似轮询强排的等待时间配置
                }
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            while (!stoppingToken.IsCancellationRequested) {
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
