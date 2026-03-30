using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Manager.Sensor;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    public class CarrierLoopGroupingHostedService : BackgroundService {
        private readonly ISensorManager _sensorManager;
        private readonly ISystemStateManager _systemStateManager;

        //小车计数变量
        public CarrierLoopGroupingHostedService(ISensorManager sensorManager,
            ISystemStateManager systemStateManager,
            IOptions<LeadshaineIoLinkageOptions> options) {
            _sensorManager = sensorManager;
            _systemStateManager = systemStateManager;

            _sensorManager.SensorStateChanged += (sender, args) => {
                if (_systemStateManager.CurrentState == SystemState.Running) {
                    //根据Sensors的配置，判断当前触发的传感器是哪个
                    //打印当前是首车触发还是,非首车触发
                    //如果是首车触发则[小车触发计数变量]=0,如果是非首车触发则[小车触发计数变量]++
                    //打印当前小车触发计数变量的值
                }
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            //1.当系统状态变成运行时，开始监控。
            while (!stoppingToken.IsCancellationRequested) {
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
