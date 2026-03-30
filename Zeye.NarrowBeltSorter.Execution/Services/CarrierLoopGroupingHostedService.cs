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

        /// <summary>
        /// 初始化小车环组统计托管服务。
        /// </summary>
        public CarrierLoopGroupingHostedService(
            ILogger<CarrierLoopGroupingHostedService> logger,
            SafeExecutor safeExecutor,
            ISensorManager sensorManager,
            ISystemStateManager systemStateManager) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _sensorManager = sensorManager ?? throw new ArgumentNullException(nameof(sensorManager));
            _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));

            _systemStateManager.StateChanged += (sender, args) => {
                _isLoadFirstCarSensor = false;
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
            if (_systemStateManager.CurrentState != SystemState.Running) {
                return;
            }

            if (args.NewState != args.TriggerState) {
                return;
            }

            if (args.SensorType is not (IoPointType.FirstCarSensor or IoPointType.NonFirstCarSensor)) {
                return;
            }

            var currentCount = 0;
            var triggerType = string.Empty;
            lock (_counterLock) {
                if (args.SensorType == IoPointType.FirstCarSensor) {
                    if (_isLoadFirstCarSensor) {
                        //建环完成
                        var originalColor = Console.ForegroundColor;
                        try {
                            Console.ForegroundColor = ConsoleColor.Green;

                            Console.WriteLine("CarrierLoopGrouping 触发类型=建环完成 SensorName={0} Point={1}",
                                args.SensorName,
                                args.Point);
                        }
                        finally {
                            Console.ForegroundColor = originalColor;
                        }
                        _isLoadFirstCarSensor = false;
                    }
                    else {
                        _isLoadFirstCarSensor = true;
                        _carrierTriggerCount = 0;
                    }

                    triggerType = "首车触发";
                }
                else {
                    if (_isLoadFirstCarSensor) {
                        _carrierTriggerCount++;
                        triggerType = "非首车触发";
                    }
                }

                currentCount = _carrierTriggerCount;
            }

            _logger.LogInformation(
                "CarrierLoopGrouping 触发类型={TriggerType} SensorName={SensorName} Point={Point} CurrentGroupIndex={CurrentGroupIndex}",
                triggerType,
                args.SensorName,
                args.Point,
                currentCount);

            var originalForegroundColor = Console.ForegroundColor;

            try {
                Console.ForegroundColor = ConsoleColor.Green;

                Console.WriteLine(
                    "CarrierLoopGrouping 触发类型={0} SensorName={1} Point={2} CurrentGroupIndex={3}",
                    triggerType,
                    args.SensorName,
                    args.Point,
                    currentCount);
            }
            finally {
                Console.ForegroundColor = originalForegroundColor;
            }
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
