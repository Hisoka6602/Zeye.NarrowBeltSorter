using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Events.Io;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Manager.Sensor;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;
using Zeye.NarrowBeltSorter.Core.Options.Carrier;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 小车环组统计托管服务：
    /// 运行状态下监听首车/非首车传感器触发，输出当前组内序号。
    /// </summary>
    public sealed class CarrierLoopGroupingHostedService : BackgroundService {
        private readonly ILogger<CarrierLoopGroupingHostedService> _logger;
        private readonly SafeExecutor _safeExecutor;
        private readonly ISensorManager _sensorManager;
        private readonly ISystemStateManager _systemStateManager;
        private readonly object _counterLock = new();
        private EventHandler<SensorStateChangedEventArgs>? _sensorStateChangedHandler;
        private int _carrierTriggerCount;
        private bool _isLoadFirstCarSensor;
        private readonly ICarrierManager _carrierManager;
        private readonly List<long> _currentRingCarrierIds = new();

        /// <summary>
        /// 初始化小车环组统计托管服务。
        /// </summary>
        public CarrierLoopGroupingHostedService(
            ILogger<CarrierLoopGroupingHostedService> logger,
            SafeExecutor safeExecutor,
            ISystemStateManager systemStateManager,
            ISensorManager sensorManager,
            ICarrierManager carrierManager,
            IOptions<CarrierManagerOptions> carrierOptions) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _sensorManager = sensorManager ?? throw new ArgumentNullException(nameof(sensorManager));
            _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));
            _carrierManager = carrierManager ?? throw new ArgumentNullException(nameof(carrierManager));
            _systemStateManager.StateChanged += (_, _) => {
                lock (_counterLock) {
                    _isLoadFirstCarSensor = false;
                    _carrierTriggerCount = 0;
                    _currentRingCarrierIds.Clear();
                }
            };
        }

        /// <summary>
        /// 挂载传感器监听并维持服务生命周期。
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            _sensorStateChangedHandler = OnSensorStateChanged;
            _sensorManager.SensorStateChanged += _sensorStateChangedHandler;

            try {
                while (!stoppingToken.IsCancellationRequested) {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) {
                // 正常停止路径。
            }
            finally {
                TryUnsubscribeSensorStateChanged();
            }
        }

        /// <summary>
        /// 停止服务并卸载事件订阅。
        /// </summary>
        public override async Task StopAsync(CancellationToken cancellationToken) {
            TryUnsubscribeSensorStateChanged();
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 传感器状态变更处理。
        /// </summary>
        private void OnSensorStateChanged(object? sender, SensorStateChangedEventArgs args) {
            _ = _safeExecutor.Execute(
                () => HandleSensorStateChanged(args),
                "CarrierLoopGroupingHostedService.OnSensorStateChanged");
        }

        /// <summary>
        /// 仅在系统运行态且命中触发电平时统计首车/非首车分组。
        /// </summary>
        private void HandleSensorStateChanged(SensorStateChangedEventArgs args) {
            if (_systemStateManager.CurrentState != SystemState.Running || args.NewState != args.TriggerState) {
                return;
            }

            if (args.SensorType is not (IoPointType.FirstCarSensor or IoPointType.NonFirstCarSensor)) {
                return;
            }

            var triggerType = string.Empty;
            var currentCount = 0;
            var currentCarrierId = 0L;
            var ringClosedCarrierIds = Array.Empty<long>();
            lock (_counterLock) {
                if (args.SensorType == IoPointType.FirstCarSensor) {
                    if (_isLoadFirstCarSensor) {
                        //建环完成
                        ringClosedCarrierIds = _currentRingCarrierIds.Distinct().ToArray();
                        _isLoadFirstCarSensor = false;
                        triggerType = "首车触发-闭环";
                    }
                    else {
                        _isLoadFirstCarSensor = true;
                        _carrierTriggerCount = 0;
                        _currentRingCarrierIds.Clear();
                        currentCarrierId = GetCurrentCarrierId(_carrierTriggerCount);
                        _currentRingCarrierIds.Add(currentCarrierId);
                        triggerType = "首车触发-开始建环";
                    }
                }
                else if (_isLoadFirstCarSensor) {
                    _carrierTriggerCount++;
                    currentCarrierId = GetCurrentCarrierId(_carrierTriggerCount);
                    _currentRingCarrierIds.Add(currentCarrierId);
                    triggerType = "非首车触发";
                }

                currentCount = _carrierTriggerCount;
            }
            if (currentCarrierId > 0) {
                _ = _safeExecutor.ExecuteAsync(
                    async () => {
                        await _carrierManager.UpdateCurrentInductionCarrierAsync(currentCarrierId).ConfigureAwait(false);
                    },
                    "CarrierLoopGroupingHostedService.UpdateCurrentInductionCarrierAsync");
            }

            if (ringClosedCarrierIds.Length > 0) {
                _ = _safeExecutor.ExecuteAsync(
                    async () => {
                        await _carrierManager
                            .BuildRingAsync(ringClosedCarrierIds, $"由首车传感器闭环触发，数量={ringClosedCarrierIds.Length}")
                            .ConfigureAwait(false);
                    },
                    "CarrierLoopGroupingHostedService.BuildRingAsync");
            }

            _logger.LogInformation(
                "CarrierLoopGrouping 触发类型={TriggerType} SensorName={SensorName} Point={Point} CurrentGroupIndex={CurrentGroupIndex} CurrentCarrierId={CurrentCarrierId}",
                triggerType,
                args.SensorName,
                args.Point,
                currentCount,
                currentCarrierId);
        }

        private static long GetCurrentCarrierId(int triggerIndex) {
            return triggerIndex + 1L;
        }

        /// <summary>
        /// 尝试卸载传感器事件订阅。
        /// </summary>
        private void TryUnsubscribeSensorStateChanged() {
            if (_sensorStateChangedHandler is null) {
                return;
            }

            _sensorManager.SensorStateChanged -= _sensorStateChangedHandler;
            _sensorStateChangedHandler = null;
        }
    }
}
