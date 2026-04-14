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
        private EventHandler<SensorStateChangedEventArgs>? _sensorStateChangedHandler;
        private readonly IOptionsMonitor<CarrierManagerOptions> _carrierOptionsMonitor;
        private readonly IDisposable _carrierOptionsChangedRegistration;
        private CarrierManagerOptions _carrierOptionsSnapshot;

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
        private readonly List<long> _currentRingCarrierIds = new();
        private readonly List<long> _builtRingCarrierIds = new();
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
            IOptionsMonitor<CarrierManagerOptions> carrierOptionsMonitor) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _sensorManager = sensorManager ?? throw new ArgumentNullException(nameof(sensorManager));
            _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));
            _carrierManager = carrierManager ?? throw new ArgumentNullException(nameof(carrierManager));
            _carrierOptionsMonitor = carrierOptionsMonitor ?? throw new ArgumentNullException(nameof(carrierOptionsMonitor));
            _carrierOptionsSnapshot = _carrierOptionsMonitor.CurrentValue ?? throw new InvalidOperationException("CarrierManagerOptions 不能为空。");
            _carrierOptionsChangedRegistration = _carrierOptionsMonitor.OnChange(RefreshCarrierOptionsSnapshot) ?? throw new InvalidOperationException("CarrierManagerOptions.OnChange 订阅失败。");
        }

        /// <summary>
        /// 当前小车管理配置快照。
        /// </summary>
        private CarrierManagerOptions CurrentCarrierOptions => Volatile.Read(ref _carrierOptionsSnapshot);

        /// <summary>
        /// 挂载传感器与系统状态监听并维持服务生命周期。
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            // 步骤1：在 ExecuteAsync 内统一订阅所有事件，确保能在 finally 中完整退订，避免内存泄漏。
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
            // 步骤1：校验运行状态与触发电平，仅处理有效触发事件。
            if ((_systemStateManager.CurrentState != SystemState.LoopTrackWarmingUp &&
       _systemStateManager.CurrentState != SystemState.Running) ||
     args.NewState != args.TriggerState) {
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
                // 步骤2：在临界区内更新建环/计数状态，确保多事件并发下状态一致。
                if (args.SensorType == IoPointType.FirstCarSensor && _builtRingCarrierIds.Count == 0) {
                    if (_isLoadFirstCarSensor) {
                        // 首车二次到达，建环闭合完成。
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
            if (currentCarrierId > 0) {
                // 步骤：解析传感器触发时间并透传，统一 CurrentInductionCarrierChanged.ChangedAt 的时间戳口径。
                // 解析失败时回退 DateTime.Now，并记录告警日志。
                DateTime? sensorOccurredAt = null;
                if (SensorTimeHelper.TryResolveLocalDateTime(args.OccurredAtMs, out var resolvedAt)) {
                    sensorOccurredAt = resolvedAt;
                }
                else {
                    _logger.LogWarning(
                        "CarrierLoopGrouping 传感器触发时间异常，将回退 DateTime.Now SensorName={SensorName} OccurredAtMs={OccurredAtMs}",
                        args.SensorName,
                        args.OccurredAtMs);
                }

                _ = _safeExecutor.ExecuteAsync(
                    () => PublishCurrentInductionCarrierAsync(currentCarrierId, sensorOccurredAt),
                    "CarrierLoopGroupingHostedService.UpdateCurrentInductionCarrierAsync");
            }

            if (ringClosedCarrierIds.Length > 0) {
                _ = _safeExecutor.ExecuteAsync(
                    () => PublishRingBuiltAsync(ringClosedCarrierIds),
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

        /// <summary>
        /// 发布当前感应位小车编号，并透传传感器触发时间用于时间戳口径统一。
        /// </summary>
        /// <param name="currentCarrierId">当前小车编号。</param>
        /// <param name="sensorOccurredAt">传感器触发时间；为 <c>null</c> 时管理器内部回退 <see cref="DateTime.Now"/>。</param>
        /// <returns>异步任务。</returns>
        private Task PublishCurrentInductionCarrierAsync(long currentCarrierId, DateTime? sensorOccurredAt) {
            return _carrierManager.UpdateCurrentInductionCarrierAsync(currentCarrierId, sensorOccurredAt).AsTask();
        }

        /// <summary>
        /// 发布建环完成事件。
        /// </summary>
        /// <param name="ringClosedCarrierIds">建环小车编号集合。</param>
        /// <returns>异步任务。</returns>
        private Task PublishRingBuiltAsync(long[] ringClosedCarrierIds) {
            return _carrierManager
                .BuildRingAsync(ringClosedCarrierIds, $"由首车传感器闭环触发，数量={ringClosedCarrierIds.Length}")
                .AsTask();
        }

        /// <summary>
        /// 将触发计数映射为当前小车编号。
        /// </summary>
        /// <param name="triggerIndex">触发计数。</param>
        /// <returns>小车编号。</returns>
        private static long GetCurrentCarrierId(int triggerIndex) {
            return triggerIndex;
        }

        /// <summary>
        /// 按原始顺序去重小车编号集合。
        /// </summary>
        /// <param name="source">源集合。</param>
        /// <returns>去重后的有序数组。</returns>
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
        /// 处理系统状态变化事件：分层次重置建环状态。
        /// 采集中间状态（_isLoadFirstCarSensor、_carrierTriggerCount、_currentRingCarrierIds）任意状态切换均重置，防止跨状态周期遗留脏数据。
        /// 已建好的环数据（_builtRingCarrierIds、_currentRingIndex）仅在急停（EmergencyStop）或故障（Faulted）时清除，避免正常停止时频繁重建环。
        /// </summary>
        private void OnSystemStateChanged(object? sender, Core.Events.System.StateChangeEventArgs args) {
            lock (_counterLock) {
                // 步骤1：任意状态变化均重置采集中间状态，防止跨状态周期遗留脏数据。
                _isLoadFirstCarSensor = false;
                _carrierTriggerCount = 0;
                _currentRingCarrierIds.Clear();

                // 步骤2：仅在急停或故障时清除已建好的环，避免正常暂停/停止时销毁已稳定的环数据。
                if (args.NewState is SystemState.EmergencyStop or SystemState.Faulted) {
                    _builtRingCarrierIds.Clear();
                    _currentRingIndex = -1;
                    _logger.LogWarning(
                        "系统进入 {NewState} 状态，已清除已建好的环数据，后续需重新触发建环流程。",
                        args.NewState);
                }
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
                    CurrentCarrierOptions.LoadingZoneCarrierOffset,
                    _carrierManager.Carriers.Count);
                _logger.LogDebug("当前上车位小车Id={LoadingZoneCarrierId}", counterClockwiseValue);
            }
        }

        /// <summary>
        /// 刷新小车管理配置快照。
        /// </summary>
        /// <param name="options">最新小车管理配置。</param>
        private void RefreshCarrierOptionsSnapshot(CarrierManagerOptions options) {
            Volatile.Write(ref _carrierOptionsSnapshot, options);
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

        /// <summary>
        /// 释放配置热更新订阅资源。
        /// </summary>
        public override void Dispose() {
            _carrierOptionsChangedRegistration.Dispose();
            base.Dispose();
        }
    }
}
