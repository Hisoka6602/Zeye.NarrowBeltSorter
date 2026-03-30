using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Models.Parcel;
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
        private readonly ConcurrentQueue<ParcelInfo> _parcelQueue = new();

        public SortingTaskOrchestrationService(
            ILogger<SortingTaskOrchestrationService> logger,
            ICarrierManager carrierManager,
            IParcelManager parcelManager,
            ISystemStateManager systemStateManager,
            IChuteManager chuteManager,
            ISensorManager sensorManager) {
            _carrierManager = carrierManager;
            _parcelManager = parcelManager;
            _systemStateManager = systemStateManager;
            _chuteManager = chuteManager;
            _sensorManager = sensorManager;

            _sensorManager.SensorStateChanged += (sender, args) => {
                if (args.SensorType == IoPointType.ParcelCreateSensor &&
                    _systemStateManager.CurrentState == SystemState.Running) {
                    //需要不能阻塞_sensorManager.SensorStateChanged发布者或其他调用者
                    _parcelManager.CreateAsync(new ParcelInfo {
                        ParcelId = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                        TargetChuteId = 0,
                    });
                }
            };

            _carrierManager.CurrentInductionCarrierChanged += (sender, args) => {
                //需要不能阻塞_carrierManager.CurrentInductionCarrierChanged发布者或其他调用者
                if (_systemStateManager.CurrentState == SystemState.Running) {
                    //通过ChuteCarrierOffsetMap算出对应的所有格口,判断所在的格口对应的小车上是否存在包裹
                    //如果存在包裹则判断目标格口是否当前格口
                    //如果目标格口是当前格口,则调用IChute.DropAsync()让小车落格
                }
            };

            _carrierManager.CarrierLoadStatusChanged += (sender, args) => {
                //打印日志,如果是上货事件则给小车分配包裹,如果是卸货事件则给格口赋值需要分拣的包裹
            };
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) {
            //我需要创建包裹后指时间取出包裹来匹配小车,例如每个包裹创建后的1500mm（包裹是不定时创建的）
            //获取当前上车位小车,给小车分配包裹,(上车事件)
            //获取包裹的目标格口，给目标格口赋值需要分拣的包裹

            throw new NotImplementedException();
        }
    }
}
