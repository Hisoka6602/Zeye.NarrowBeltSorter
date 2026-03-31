using System;
using System.Linq;
using System.Threading;
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
        private CancellationToken _serviceStoppingToken;
        private EventHandler<SensorStateChangedEventArgs>? _sensorStateChangedHandler;

        /// <summary>
        /// 系统状态变化事件处理器缓存，用于退订时精准移除。
        /// </summary>
        private EventHandler<Core.Events.System.StateChangeEventArgs>? _stateChangedHandler;

        /// <summary>
        /// 感应位小车变化事件处理器缓存，用于退订时精准移除。
        /// </summary>
        private EventHandler<Core.Events.Carrier.CurrentInductionCarrierChangedEventArgs>? _inductionCarrierChangedHandler;

        private int _carrierTriggerCount;
        private bool _isLoadFirstCarSensor;
        private readonly ICarrierManager _carrierManager;
        private readonly int _loadingZoneCarrierOffset;
        private readonly List<long> _currentRingCarrierIds = new();
        private readonly List<long> _builtRingCarrierIds = new();

        /// <summary>
        /// 传感器事件串行门控：确保高密度触发场景下 BuildRing 与 UpdateCurrentInductionCarrier
        /// 按触发顺序串行执行，消除 fire-and-forget 并发乱序导致的 CarrierId 偏差。
        /// </summary>
        private readonly SemaphoreSlim _sensorEventGate = new(1, 1);

        private int _currentRingIndex = -1;

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
            _loadingZoneCarrierOffset = carrierOptions?.Value?.LoadingZoneCarrierOffset
                ?? throw new ArgumentNullException(nameof(carrierOptions));
        }

        /// <summary>
        /// 挂载传感器与系统状态监听并维持服务生命周期。
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            // 步骤1：在 ExecuteAsync 内统一订阅所有事件，确保能在 finally 中完整退订，避免内存泄漏。
            _serviceStoppingToken = stoppingToken;
            _sensorStateChangedHandler = OnSensorStateChanged;
            _stateChangedHandler = OnSystemStateChanged;
            _inductionCarrierChangedHandler = OnCurrentInductionCarrierChanged;

            _sensorManager.SensorStateChanged += _sensorStateChangedHandler;
            _systemStateManager.StateChanged += _stateChangedHandler;
            _carrierManager.CurrentInductionCarrierChanged += _inductionCarrierChangedHandler;

            try {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // 正常停止路径。
            }
            finally {
                TryUnsubscribeAll();
            }
        }

        /// <summary>
        /// 停止服务并卸载事件订阅。
        /// </summary>
        public override async Task StopAsync(CancellationToken cancellationToken) {
            TryUnsubscribeAll();
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override void Dispose() {
            // 仅调用基类 Dispose，不显式释放 _sensorEventGate，
            // 避免仍在执行的传感器处理任务调用 WaitAsync/Release 时抛 ObjectDisposedException。
            // 宿主进程退出时 GC 负责最终清理。
            base.Dispose();
        }

        /// <summary>
        /// 传感器状态变更处理。
        /// </summary>
        private void OnSensorStateChanged(object? sender, SensorStateChangedEventArgs args) {
            _ = _safeExecutor.ExecuteAsync(
                cancellationToken => HandleSensorStateChangedAsync(args, cancellationToken),
                "CarrierLoopGroupingHostedService.OnSensorStateChanged",
                _serviceStoppingToken);
        }

        /// <summary>
        /// 仅在系统运行态且命中触发电平时统计首车/非首车分组。
        /// 通过 _sensorEventGate 串行门控，确保多传感器高密度触发时 BuildRing 与
        /// UpdateCurrentInductionCarrier 按触发顺序执行，消除 fire-and-forget 并发乱序。
        /// </summary>
        private async ValueTask HandleSensorStateChangedAsync(SensorStateChangedEventArgs args, CancellationToken cancellationToken) {
            if (_systemStateManager.CurrentState != SystemState.Running || args.NewState != args.TriggerState) {
                return;
            }

            if (args.SensorType is not (IoPointType.FirstCarSensor or IoPointType.NonFirstCarSensor)) {
                return;
            }

            // 步骤1：进入串行门控，确保同一时刻只有一个传感器触发在执行后续异步链路。
            // 服务正常停止时 cancellationToken 取消，捕获后直接返回，不视为错误。
            try {
                await _sensorEventGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                return;
            }

            try {
                // 步骤2：在锁内根据触发类型计算本次应更新的小车号和建环数据。
                var triggerType = string.Empty;
                var currentCount = 0;
                var currentCarrierId = 0L;
                var ringClosedCarrierIds = Array.Empty<long>();
                lock (_counterLock) {
                    if (args.SensorType == IoPointType.FirstCarSensor && _builtRingCarrierIds.Count == 0) {
                        if (_isLoadFirstCarSensor) {
                            ringClosedCarrierIds = DistinctPreserveOrder(_currentRingCarrierIds);
                            _builtRingCarrierIds.Clear();
                            _builtRingCarrierIds.AddRange(ringClosedCarrierIds);
                            _currentRingIndex = _builtRingCarrierIds.Count > 0 ? 0 : -1;
                            if (_currentRingIndex >= 0) {
                                currentCarrierId = _builtRingCarrierIds[_currentRingIndex];
                            }
                            _isLoadFirstCarSensor = false;
                            triggerType = "首车触发-闭环并发布当前小车";
                        }
                        else {
                            _isLoadFirstCarSensor = true;
                            _carrierTriggerCount = 0;
                            _currentRingCarrierIds.Clear();
                            triggerType = "首车触发-开始建环";
                        }
                    }
                    else if (_isLoadFirstCarSensor) {
                        _carrierTriggerCount++;
                        currentCarrierId = GetCurrentCarrierId(_carrierTriggerCount);
                        if (_carrierTriggerCount > 0) {
                            _currentRingCarrierIds.Add(currentCarrierId);
                        }

                        triggerType = "非首车触发";
                    }
                    else if (_builtRingCarrierIds.Count > 0) {
                        _currentRingIndex = (_currentRingIndex + 1 + _builtRingCarrierIds.Count) % _builtRingCarrierIds.Count;
                        currentCarrierId = _builtRingCarrierIds[_currentRingIndex];
                        triggerType = args.SensorType == IoPointType.FirstCarSensor
                            ? "首车触发-环运行"
                            : "非首车触发-环运行";
                    }

                    currentCount = _carrierTriggerCount;
                }

                // 步骤3：建环闭合时先 BuildRing 再 UpdateCurrentInductionCarrier，
                // 确保上车链路读到完整的 Carriers 列表，消除建环竞争窗口。
                if (ringClosedCarrierIds.Length > 0) {
                    await _carrierManager
                        .BuildRingAsync(ringClosedCarrierIds, $"由首车传感器闭环触发，数量={ringClosedCarrierIds.Length}")
                        .ConfigureAwait(false);
                    _logger.LogInformation("CarrierLoopGrouping 建环完成 CarrierCount={CarrierCount}", ringClosedCarrierIds.Length);
                }

                // 步骤4：在门控内顺序更新感应位小车号，保证写入顺序与触发顺序一致。
                if (currentCarrierId > 0) {
                    await _carrierManager
                        .UpdateCurrentInductionCarrierAsync(currentCarrierId)
                        .ConfigureAwait(false);
                }

                _logger.LogInformation(
                    "CarrierLoopGrouping 触发类型={TriggerType} SensorName={SensorName} Point={Point} CurrentGroupIndex={CurrentGroupIndex} CurrentCarrierId={CurrentCarrierId}",
                    triggerType,
                    args.SensorName,
                    args.Point,
                    currentCount,
                    currentCarrierId);
            }
            finally {
                // 步骤5：无论成功还是异常，均释放门控，确保后续触发不被永久阻塞。
                _sensorEventGate.Release();
            }
        }

        private static long GetCurrentCarrierId(int triggerIndex) {
            return triggerIndex;
        }

        private static long[] DistinctPreserveOrder(IEnumerable<long> source) {
            var result = new List<long>();
            var seen = new HashSet<long>();
            foreach (var id in source) {
                if (seen.Add(id)) {
                    result.Add(id);
                }
            }

            return result.ToArray();
        }

        /// <summary>
        /// 处理系统状态变化事件：重置建环状态，防止跨状态周期引入脏数据。
        /// </summary>
        private void OnSystemStateChanged(object? sender, Core.Events.System.StateChangeEventArgs args) {
            lock (_counterLock) {
                _isLoadFirstCarSensor = false;
                _carrierTriggerCount = 0;
                _currentRingCarrierIds.Clear();
                _builtRingCarrierIds.Clear();
                _currentRingIndex = -1;
            }
        }

        /// <summary>
        /// 处理感应位小车变化事件：记录当前感应位与上车位小车编号。
        /// </summary>
        private void OnCurrentInductionCarrierChanged(object? sender, Core.Events.Carrier.CurrentInductionCarrierChangedEventArgs args) {
            _logger.LogDebug("当前感应位小车Id={NewCarrierId}", args.NewCarrierId);

            if (_carrierManager is { IsRingBuilt: true, Carriers.Count: > 0 }) {
                var counterClockwiseValue = CircularValueHelper.GetCounterClockwiseValue(
                    (int)(args.NewCarrierId ?? 1),
                    _loadingZoneCarrierOffset,
                    _carrierManager.Carriers.Count);
                _logger.LogDebug("当前上车位小车Id={LoadingZoneCarrierId}", counterClockwiseValue);
            }
        }

        /// <summary>
        /// 退订所有事件，防止处理器持有引用导致内存泄漏。
        /// </summary>
        private void TryUnsubscribeAll() {
            if (_sensorStateChangedHandler is not null) {
                _sensorManager.SensorStateChanged -= _sensorStateChangedHandler;
                _sensorStateChangedHandler = null;
            }

            if (_stateChangedHandler is not null) {
                _systemStateManager.StateChanged -= _stateChangedHandler;
                _stateChangedHandler = null;
            }

            if (_inductionCarrierChangedHandler is not null) {
                _carrierManager.CurrentInductionCarrierChanged -= _inductionCarrierChangedHandler;
                _inductionCarrierChangedHandler = null;
            }
        }
    }
}
