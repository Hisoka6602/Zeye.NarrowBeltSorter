using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Sensor;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    public class SortingTaskOrchestrationService : BackgroundService {
        private readonly ICarrierManager _carrierManager;
        private readonly IParcelManager _parcelManager;
        private readonly ISystemStateManager _systemStateManager;
        private readonly IChuteManager _chuteManager;
        private readonly ISensorManager _sensorManager;

        public SortingTaskOrchestrationService(
            ILogger<SortingTaskOrchestrationService> logger,
            ICarrierManager carrierManager,
            IParcelManager parcelManager,
            ISystemStateManager systemStateManager,
            IChuteManager chuteManager,
            ISensorManager sensorManager
            ) {
            _carrierManager = carrierManager;
            _parcelManager = parcelManager;
            _systemStateManager = systemStateManager;
            _chuteManager = chuteManager;
            _sensorManager = sensorManager;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) {
            throw new NotImplementedException();
        }
    }
}
