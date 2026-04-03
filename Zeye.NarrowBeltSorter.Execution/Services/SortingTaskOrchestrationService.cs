using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Models.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Sensor;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Options.Sorting;
using Zeye.NarrowBeltSorter.Core.Enums.Sorting;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 分拣任务编排服务（主协调器）：
    /// 1) 包裹创建与成熟泵送；
    /// 2) 上车编排委托给 SortingTaskCarrierLoadingService；
    /// 3) 落格编排委托给 SortingTaskDropOrchestrationService。
    /// </summary>
    public sealed class SortingTaskOrchestrationService : BackgroundService {

        /// <summary>
        /// 传感器事件毫秒时间戳可转换为 DateTime 的最大值（本地时间语义）。
        /// </summary>
        private static readonly long MaxSensorOccurredAtMs = DateTime.MaxValue.Ticks / TimeSpan.TicksPerMillisecond;

        /// <summary>
        /// 日志记录器。
        /// </summary>
        private readonly ILogger<SortingTaskOrchestrationService> _logger;

        /// <summary>
        /// 统一安全执行器。
        /// </summary>
        private readonly SafeExecutor _safeExecutor;

        /// <summary>
        /// 包裹管理器。
        /// </summary>
        private readonly IParcelManager _parcelManager;

        /// <summary>
        /// 系统状态管理器。
        /// </summary>
        private readonly ISystemStateManager _systemStateManager;

        /// <summary>
        /// 传感器管理器。
        /// </summary>
        private readonly ISensorManager _sensorManager;

        /// <summary>
        /// 小车管理器（直接订阅小车事件，避免中间层透传）。
        /// </summary>
        private readonly ICarrierManager _carrierManager;

        /// <summary>
        /// 上车编排服务。
        /// </summary>
        private readonly SortingTaskCarrierLoadingService _carrierLoadingService;

        /// <summary>
        /// 落格编排服务。
        /// </summary>
        private readonly SortingTaskDropOrchestrationService _dropOrchestrationService;

        /// <summary>
        /// 分拣时序配置监视器（支持热更新）。
        /// </summary>
        private readonly IOptionsMonitor<SortingTaskTimingOptions> _sortingTaskTimingOptionsMonitor;

        /// <summary>
        /// 原始包裹队列。
        /// </summary>
        private readonly ConcurrentQueue<ParcelInfo> _rawParcelQueue = new();

        /// <summary>
        /// 原始包裹入队信号量。
        /// </summary>
        private readonly SemaphoreSlim _parcelSignal = new(0);

        /// <summary>
        /// 传感器状态变化事件处理器缓存。
        /// </summary>
        private EventHandler<Core.Events.Io.SensorStateChangedEventArgs>? _sensorStateChangedHandler;

        /// <summary>
        /// 最近一次生成的包裹编号（Ticks），用于同毫秒触发下的唯一性补偿。
        /// </summary>
        private long _lastGeneratedParcelIdTicks;

        /// <summary>
        /// 最近一次上车触发源触发时间（本地时间）。
        /// </summary>
        private DateTime? _lastLoadingTriggerOccurredAt;

        /// <summary>
        /// 上车触发源时间读写锁。
        /// </summary>
        private readonly object _loadingTriggerSync = new();

        /// <summary>
        /// 包裹成熟起始时间映射（键：ParcelId；值：起始时间）。
        /// </summary>
        private readonly ConcurrentDictionary<long, DateTime> _parcelMatureStartAtMap = new();

        /// <summary>
        /// 当前感应位小车变化事件处理器缓存。
        /// </summary>
        private EventHandler<Core.Events.Carrier.CurrentInductionCarrierChangedEventArgs>? _currentInductionCarrierChangedHandler;

        /// <summary>
        /// 小车装载状态变化事件处理器缓存。
        /// </summary>
        private EventHandler<Core.Events.Carrier.CarrierLoadStatusChangedEventArgs>? _carrierLoadStatusChangedHandler;

        /// <summary>
        /// 包裹目标格口更新事件处理器缓存。
        /// </summary>
        private EventHandler<Core.Events.Parcel.ParcelTargetChuteUpdatedEventArgs>? _parcelTargetChuteUpdatedHandler;

        /// <summary>
        /// 初始化分拣任务编排服务。
        /// </summary>
        public SortingTaskOrchestrationService(
            ILogger<SortingTaskOrchestrationService> logger,
            SafeExecutor safeExecutor,
            IParcelManager parcelManager,
            ISystemStateManager systemStateManager,
            ISensorManager sensorManager,
            ICarrierManager carrierManager,
            SortingTaskCarrierLoadingService carrierLoadingService,
            SortingTaskDropOrchestrationService dropOrchestrationService,
            IOptionsMonitor<SortingTaskTimingOptions> sortingTaskTimingOptionsMonitor) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _parcelManager = parcelManager ?? throw new ArgumentNullException(nameof(parcelManager));
            _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));
            _sensorManager = sensorManager ?? throw new ArgumentNullException(nameof(sensorManager));
            _carrierManager = carrierManager ?? throw new ArgumentNullException(nameof(carrierManager));
            _carrierLoadingService = carrierLoadingService ?? throw new ArgumentNullException(nameof(carrierLoadingService));
            _dropOrchestrationService = dropOrchestrationService ?? throw new ArgumentNullException(nameof(dropOrchestrationService));
            _sortingTaskTimingOptionsMonitor = sortingTaskTimingOptionsMonitor ?? throw new ArgumentNullException(nameof(sortingTaskTimingOptionsMonitor));
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            SubscribeEvents();

            try {
                await PumpRawQueueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // 宿主正常停止。
            }
            finally {
                UnsubscribeEvents();
            }
        }

        /// <inheritdoc />
        public override async Task StopAsync(CancellationToken cancellationToken) {
            UnsubscribeEvents();
            _parcelSignal.Release();
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
            _parcelMatureStartAtMap.Clear();
        }

        /// <summary>
        /// 订阅业务事件。
        /// </summary>
        private void SubscribeEvents() {
            _sensorStateChangedHandler ??= OnSensorStateChanged;
            _currentInductionCarrierChangedHandler ??= OnCurrentInductionCarrierChanged;
            _carrierLoadStatusChangedHandler ??= OnCarrierLoadStatusChanged;
            _parcelTargetChuteUpdatedHandler ??= OnParcelTargetChuteUpdated;

            _sensorManager.SensorStateChanged += _sensorStateChangedHandler;
            _carrierManager.CurrentInductionCarrierChanged += _currentInductionCarrierChangedHandler;
            _carrierManager.CarrierLoadStatusChanged += _carrierLoadStatusChangedHandler;
            _parcelManager.ParcelTargetChuteUpdated += _parcelTargetChuteUpdatedHandler;
        }

        /// <summary>
        /// 取消订阅业务事件。
        /// </summary>
        private void UnsubscribeEvents() {
            if (_sensorStateChangedHandler is not null) {
                _sensorManager.SensorStateChanged -= _sensorStateChangedHandler;
            }

            if (_currentInductionCarrierChangedHandler is not null) {
                _carrierManager.CurrentInductionCarrierChanged -= _currentInductionCarrierChangedHandler;
            }

            if (_carrierLoadStatusChangedHandler is not null) {
                _carrierManager.CarrierLoadStatusChanged -= _carrierLoadStatusChangedHandler;
            }

            if (_parcelTargetChuteUpdatedHandler is not null) {
                _parcelManager.ParcelTargetChuteUpdated -= _parcelTargetChuteUpdatedHandler;
            }

            _sensorStateChangedHandler = null;
            _currentInductionCarrierChangedHandler = null;
            _carrierLoadStatusChangedHandler = null;
            _parcelTargetChuteUpdatedHandler = null;
        }

        /// <summary>
        /// 将原始包裹队列中的包裹按成熟时间转移到待装车队列。
        /// </summary>
        private async Task PumpRawQueueAsync(CancellationToken stoppingToken) {
            while (!stoppingToken.IsCancellationRequested) {
                await _parcelSignal.WaitAsync(stoppingToken).ConfigureAwait(false);
                var safeParcelMatureDelayMs = ConfigurationValueHelper.GetPositiveOrDefault(
                    _sortingTaskTimingOptionsMonitor.CurrentValue.ParcelMatureDelayMs,
                    SortingTaskTimingOptions.DefaultParcelMatureDelayMs);

                while (_rawParcelQueue.TryPeek(out var headParcel)) {
                    var now = DateTime.Now;
                    var matureAt = GetParcelMatureAt(
                        headParcel.ParcelId,
                        safeParcelMatureDelayMs);
                    if (matureAt > now) {
                        await Task.Delay(matureAt - now, stoppingToken).ConfigureAwait(false);
                    }

                    if (_rawParcelQueue.TryDequeue(out var readyParcel)) {
                        // 步骤：包裹进入待装车队列后，不再依赖成熟起始时间映射，及时释放内存占用。
                        _parcelMatureStartAtMap.TryRemove(readyParcel.ParcelId, out _);
                        _carrierLoadingService.EnqueueReadyParcel(readyParcel);
                        _logger.LogInformation(
                            "包裹进入待装车队列 ParcelId={ParcelId} ReadyQueueCount={QueueCount}",
                            readyParcel.ParcelId,
                            _carrierLoadingService.ReadyQueueCount);
                    }
                }
            }
        }

        /// <summary>
        /// 处理传感器状态变化事件。
        /// </summary>
        private void OnSensorStateChanged(object? sender, Core.Events.Io.SensorStateChangedEventArgs args) {
            _ = _safeExecutor.ExecuteAsync(
                () => HandleSensorStateChangedAsync(args),
                "SortingTaskOrchestrationService.OnSensorStateChanged");
        }

        /// <summary>
        /// 处理小车装载状态变化事件。
        /// </summary>
        private void OnCarrierLoadStatusChanged(object? sender, Core.Events.Carrier.CarrierLoadStatusChangedEventArgs args) {
            var currentState = _systemStateManager.CurrentState;
            _ = _safeExecutor.ExecuteAsync(
                token => _carrierLoadingService.HandleCarrierLoadStatusChangedAsync(args, currentState, token),
                "SortingTaskOrchestrationService.OnCarrierLoadStatusChanged");
        }

        /// <summary>
        /// 处理当前感应位小车变化事件。
        /// </summary>
        private void OnCurrentInductionCarrierChanged(object? sender, Core.Events.Carrier.CurrentInductionCarrierChangedEventArgs args) {
            var currentState = _systemStateManager.CurrentState;
            _ = _safeExecutor.ExecuteAsync(
                token => _dropOrchestrationService.HandleCurrentInductionCarrierChangedAsync(args, currentState, token),
                "SortingTaskOrchestrationService.OnCurrentInductionCarrierChanged");
        }

        /// <summary>
        /// 处理包裹目标格口更新事件，并记录分拣编排日志。
        /// </summary>
        /// <param name="sender">事件发送方。</param>
        /// <param name="args">事件参数。</param>
        private void OnParcelTargetChuteUpdated(object? sender, Core.Events.Parcel.ParcelTargetChuteUpdatedEventArgs args) {
            _logger.LogInformation(
                "赋值目标格口 ParcelId={ParcelId} OldTargetChuteId={OldTargetChuteId} NewTargetChuteId={NewTargetChuteId} AssignedAt={AssignedAt}",
                args.ParcelId,
                args.OldTargetChuteId,
                args.NewTargetChuteId,
                args.AssignedAt);
        }

        /// <summary>
        /// 安全执行传感器状态变化业务逻辑。
        /// </summary>
        private async Task HandleSensorStateChangedAsync(Core.Events.Io.SensorStateChangedEventArgs args) {
            if (args.NewState != args.TriggerState) {
                return;
            }

            if (_systemStateManager.CurrentState != SystemState.Running) {
                return;
            }

            if (args.SensorType == IoPointType.LoadingTriggerSensor) {
                UpdateLoadingTriggerOccurredAt(args.OccurredAtMs);
                return;
            }

            if (args.SensorType != IoPointType.ParcelCreateSensor) {
                return;
            }

            var parcelId = GenerateParcelIdTicksFromSensorEvent(args.OccurredAtMs);
            var matureStartAt = ResolveParcelMatureStartAt(parcelId);
            var parcel = new ParcelInfo {
                ParcelId = parcelId,
            };

            var created = await _parcelManager.CreateAsync(parcel).ConfigureAwait(false);
            if (!created) {
                _logger.LogWarning("创建包裹失败或包裹已存在 ParcelId={ParcelId}", parcelId);
                return;
            }

            _parcelMatureStartAtMap[parcelId] = matureStartAt;
            _rawParcelQueue.Enqueue(parcel);
            _parcelSignal.Release();
            _logger.LogInformation(
                "创建包裹成功并入原始队列 ParcelId={ParcelId} MatureStartAt={MatureStartAt:O}",
                parcelId,
                matureStartAt);
        }

        /// <summary>
        /// 解析包裹成熟时间起始点。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <returns>成熟时间起始点。</returns>
        private DateTime ResolveParcelMatureStartAt(long parcelId) {
            var timingOptions = _sortingTaskTimingOptionsMonitor.CurrentValue;
            var configuredStartSource = timingOptions.ParcelMatureStartSource;
            var startSource = configuredStartSource switch {
                ParcelMatureStartSource.ParcelCreateSensor => ParcelMatureStartSource.ParcelCreateSensor,
                ParcelMatureStartSource.LoadingTriggerSensor => ParcelMatureStartSource.LoadingTriggerSensor,
                _ => SortingTaskTimingOptions.DefaultParcelMatureStartSource
            };

            if (startSource == ParcelMatureStartSource.ParcelCreateSensor) {
                return new DateTime(parcelId, DateTimeKind.Local);
            }

            var loadingTriggerOccurredAt = GetLastLoadingTriggerOccurredAt();
            if (loadingTriggerOccurredAt.HasValue) {
                return loadingTriggerOccurredAt.Value;
            }

            if (timingOptions.EnableFallbackToParcelCreateWhenLoadingTriggerMissing) {
                _logger.LogWarning(
                    "上车触发源缺失，按配置回退创建包裹触发源 ParcelId={ParcelId}",
                    parcelId);
                return new DateTime(parcelId, DateTimeKind.Local);
            }

            _logger.LogWarning(
                "上车触发源缺失且未启用回退，改为使用本地当前时间计算成熟时间（可能与实际创建时间存在偏差） ParcelId={ParcelId}",
                parcelId);
            return DateTime.Now;
        }

        /// <summary>
        /// 更新最近一次上车触发源触发时间。
        /// </summary>
        /// <param name="occurredAtMs">传感器触发时间毫秒值。</param>
        private void UpdateLoadingTriggerOccurredAt(long occurredAtMs) {
            var loadingTriggerOccurredAt = ResolveLocalDateTimeFromSensorOccurredAtMs(occurredAtMs, "上车触发源");
            lock (_loadingTriggerSync) {
                _lastLoadingTriggerOccurredAt = loadingTriggerOccurredAt;
            }

            _logger.LogDebug(
                "更新上车触发源时间 LoadingTriggerOccurredAt={LoadingTriggerOccurredAt:O}",
                loadingTriggerOccurredAt);
        }

        /// <summary>
        /// 获取最近一次上车触发源触发时间。
        /// </summary>
        /// <returns>触发时间；无记录时返回 null。</returns>
        private DateTime? GetLastLoadingTriggerOccurredAt() {
            lock (_loadingTriggerSync) {
                return _lastLoadingTriggerOccurredAt;
            }
        }

        /// <summary>
        /// 将传感器触发毫秒值解析为本地时间。
        /// </summary>
        /// <param name="occurredAtMs">传感器触发毫秒值。</param>
        /// <param name="sensorName">传感器名称。</param>
        /// <returns>本地时间。</returns>
        private DateTime ResolveLocalDateTimeFromSensorOccurredAtMs(long occurredAtMs, string sensorName) {
            if (occurredAtMs > 0 && occurredAtMs <= MaxSensorOccurredAtMs) {
                return new DateTime(occurredAtMs * TimeSpan.TicksPerMillisecond, DateTimeKind.Local);
            }

            _logger.LogWarning(
                "{SensorName} 事件时间异常，回退本地当前时间 OccurredAtMs={OccurredAtMs} MaxAllowedMs={MaxAllowedMs}",
                sensorName,
                occurredAtMs,
                MaxSensorOccurredAtMs);
            return DateTime.Now;
        }

        /// <summary>
        /// 根据包裹编号计算包裹成熟时间。
        /// </summary>
        private DateTime GetParcelMatureAt(long parcelId, int parcelMatureDelayMs) {
            if (!_parcelMatureStartAtMap.TryGetValue(parcelId, out var matureStartAt)) {
                _logger.LogWarning(
                    "包裹成熟起始时间映射缺失，按当前配置回退计算成熟起始时间 ParcelId={ParcelId}",
                    parcelId);
                matureStartAt = ResolveParcelMatureStartAt(parcelId);
            }

            return matureStartAt + TimeSpan.FromMilliseconds(parcelMatureDelayMs);
        }

        /// <summary>
        /// 基于传感器事件时间生成包裹编号（Ticks），并在同毫秒并发触发时保证唯一性。
        /// </summary>
        /// <param name="occurredAtMs">
        /// 传感器事件发生时间（以 DateTime 基准 0001-01-01 为起点的本地时间毫秒时间戳）。
        /// 参数值应来源于本地时间 DateTime 的 Ticks 换算，例如：DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond。
        /// 禁止传入以 Unix Epoch（1970-01-01）或其他相对时基计算的毫秒时间戳，以避免时间严重偏移。
        /// </param>
        /// <returns>可用于后续成熟时间计算的本地时间 Ticks 编码编号。</returns>
        private long GenerateParcelIdTicksFromSensorEvent(long occurredAtMs) {
            // 步骤1：优先使用传感器事件时间构造本地时间，异常值回退到本地当前时间并记录告警日志。
            var sensorTriggeredAt = ResolveLocalDateTimeFromSensorOccurredAtMs(occurredAtMs, "创建包裹触发源");
            var candidateTicks = sensorTriggeredAt.Ticks;
            var spinWait = new SpinWait();

            // 步骤2：通过 CAS 自旋确保并发下编号单调递增且唯一，并在达到上界时执行保护回退。
            while (true) {
                var last = Volatile.Read(ref _lastGeneratedParcelIdTicks);
                if (last >= DateTime.MaxValue.Ticks) {
                    _logger.LogError(
                        "包裹编号 Ticks 已达到 DateTime.MaxValue 上限，启用上界保护 LastTicks={LastTicks}",
                        last);
                    return DateTime.MaxValue.Ticks;
                }

                var next = candidateTicks > last ? candidateTicks : last + 1;
                var previous = Interlocked.CompareExchange(ref _lastGeneratedParcelIdTicks, next, last);
                if (previous == last) {
                    return next;
                }

                if (previous >= DateTime.MaxValue.Ticks) {
                    _logger.LogError(
                        "包裹编号 Ticks 在并发竞争中达到 DateTime.MaxValue 上限，启用上界保护 PreviousTicks={PreviousTicks}",
                        previous);
                    return DateTime.MaxValue.Ticks;
                }

                candidateTicks = previous + 1;
                spinWait.SpinOnce();
            }
        }
    }
}
