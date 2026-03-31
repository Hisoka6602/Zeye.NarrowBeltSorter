using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Manager.Sensor;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;
using Zeye.NarrowBeltSorter.Core.Options.Carrier;
using Zeye.NarrowBeltSorter.Core.Enums.SignalTower;
using Zeye.NarrowBeltSorter.Core.Manager.SignalTower;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    public class SignalTowerHostedService : BackgroundService {
        private readonly ILogger<SignalTowerHostedService> _logger;
        private readonly SafeExecutor _safeExecutor;
        private readonly ISystemStateManager _systemStateManager;
        private readonly ISensorManager _sensorManager;
        private readonly ICarrierManager _carrierManager;
        private readonly ISignalTower _signalTower;
        private readonly IOptions<LeadshaineIoPanelStateTransitionOptions> _options;

        public SignalTowerHostedService(ILogger<SignalTowerHostedService> logger,
            SafeExecutor safeExecutor,
            ISystemStateManager systemStateManager,
            ISensorManager sensorManager,
            ICarrierManager carrierManager,
            ISignalTower signalTower,
            IOptions<LeadshaineIoPanelStateTransitionOptions> options) {
            _logger = logger;
            _safeExecutor = safeExecutor;
            _systemStateManager = systemStateManager;
            _sensorManager = sensorManager;
            _carrierManager = carrierManager;
            _signalTower = signalTower;
            _options = options;

            _systemStateManager.StateChanged += (sender, args) => {
                //状态改变时执行的代码
                if (args.NewState == SystemState.Paused || args.NewState == SystemState.Booting || args.NewState == SystemState.Ready) {
                    _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Off);
                }
                else if (args.NewState == SystemState.EmergencyStop) {
                    _ = _safeExecutor.ExecuteAsync(async () => {
                        await _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Red);

                        await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.On);
                        await Task.Delay(2000);
                        await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.Off);
                    }, "");
                }
                else if (args.NewState == SystemState.StartupWarning || args.NewState == SystemState.Ready) {
                    _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Yellow);
                }
            };

            _carrierManager.RingBuilt += (sender, args) => {
                _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Green);
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            while (!stoppingToken.IsCancellationRequested) {
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
