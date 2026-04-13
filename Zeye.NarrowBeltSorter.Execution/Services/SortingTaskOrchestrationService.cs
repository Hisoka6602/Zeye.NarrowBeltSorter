using System;
using System.Threading.Tasks;
using System.Threading.Channels;
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
        /// 传感器事件有序通道容量（条）。
        /// </summary>
        private const int SensorEventChannelCapacity = 1024;

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
        private readonly IDisposable _timingOptionsChangedRegistration;
        private SortingTaskTimingOptions _timingOptionsSnapshot;

        /// <summary>
        /// 原始包裹队列。
        /// </summary>
        private readonly ConcurrentQueue<ParcelInfo> _rawParcelQueue = new();
        private int _rawQueueCount;

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
        /// 待绑定上车触发的包裹编号队列（FIFO）。
        /// </summary>
        private readonly ConcurrentQueue<long> _pendingLoadingTriggerParcelIdQueue = new();

        /// <summary>
        /// 待绑定上车触发的包裹集合（用于并发去重与快速判定）。
        /// </summary>
        private readonly ConcurrentDictionary<long, byte> _waitingLoadingTriggerParcelSet = new();

        /// <summary>
        /// 当前待绑定上车触发包裹数量（原子计数，与 _waitingLoadingTriggerParcelSet 同步维护）。
        /// </summary>
        private int _waitingLoadingTriggerParcelCount;

        /// <summary>
        /// 包裹成熟起始时间映射（键：ParcelId；值：起始时间）。
        /// </summary>
        private readonly ConcurrentDictionary<long, DateTime> _parcelMatureStartAtMap = new();

        /// <summary>
        /// 丢失包裹集合（已判定超窗，不再参与上车）。
        /// </summary>
        private readonly ConcurrentDictionary<long, byte> _lostParcelIdSet = new();

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
        /// 系统状态变化事件处理器缓存。
        /// </summary>
        private EventHandler<Core.Events.System.StateChangeEventArgs>? _systemStateChangedHandler;

        /// <summary>
        /// 传感器事件有序通道。
        /// 所有传感器状态变化事件均写入此有界通道，由专属消费者按 FIFO 顺序串行处理，
        /// 消除高频密集场景下线程池调度导致的创建包裹与上车触发事件相对乱序问题（Phase 3.2）。
        /// 通道满时丢弃最新写入并记录告警，发布者始终非阻塞。
        /// </summary>
        private readonly Channel<Core.Events.Io.SensorStateChangedEventArgs> _sensorEventChannel =
            Channel.CreateBounded<Core.Events.Io.SensorStateChangedEventArgs>(
                new BoundedChannelOptions(SensorEventChannelCapacity) {
                    FullMode = BoundedChannelFullMode.DropWrite,
                    SingleReader = true,
                    SingleWriter = false
                });

        /// <summary>
        /// 传感器事件通道关闭标志。
        /// 置位后 TryWrite 返回 false 属正常关闭流程，降级为 Debug 日志，不视为满载丢弃。
        /// 通过 Volatile.Read/Write 显式访问，与代码库其他原子字段保持一致。
        /// </summary>
        private bool _sensorEventChannelCompleted;

        /// <summary>
        /// 传感器事件通道累计丢弃事件数（通道真正满载时递增，用于限频告警聚合）。
        /// </summary>
        private long _droppedSensorEventCount;

        /// <summary>
        /// 传感器事件通道最近一次输出丢弃告警的时间刻（毫秒，Environment.TickCount64，用于每秒最多一次的限频判断）。
        /// </summary>
        private long _lastDropWarningElapsedMs;

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
            _timingOptionsSnapshot = _sortingTaskTimingOptionsMonitor.CurrentValue ?? throw new InvalidOperationException("SortingTaskTimingOptions 不能为空。");
            _timingOptionsChangedRegistration = _sortingTaskTimingOptionsMonitor.OnChange(RefreshTimingOptionsSnapshot) ?? throw new InvalidOperationException("SortingTaskTimingOptions.OnChange 订阅失败。");
        }

        /// <summary>
        /// 当前分拣时序配置快照。
        /// </summary>
        private SortingTaskTimingOptions CurrentTimingOptions {
            get {
                var snapshot = Volatile.Read(ref _timingOptionsSnapshot);
                if (snapshot is not null) {
                    return snapshot;
                }

                var monitorSnapshot = _sortingTaskTimingOptionsMonitor?.CurrentValue;
                if (monitorSnapshot is not null) {
                    Volatile.Write(ref _timingOptionsSnapshot, monitorSnapshot);
                    return monitorSnapshot;
                }

                return new SortingTaskTimingOptions();
            }
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            SubscribeEvents();

            try {
                // 步骤：并行运行包裹成熟泵送循环与传感器事件有序消费者，两者独立推进互不阻塞。
                await Task.WhenAll(
                    PumpRawQueueAsync(stoppingToken),
                    ConsumeSensorEventChannelAsync(stoppingToken)
                ).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // 宿主正常停止。
            }
            finally {
                // 步骤：先取消事件订阅，再标记通道关闭并 Complete，避免取消订阅前后窗口内的事件
                // 在通道已完成后写入产生"通道已满"误告警。
                UnsubscribeEvents();
                Volatile.Write(ref _sensorEventChannelCompleted, true);
                _sensorEventChannel.Writer.TryComplete();
            }
        }

        /// <inheritdoc />
        public override async Task StopAsync(CancellationToken cancellationToken) {
            UnsubscribeEvents();
            _parcelSignal.Release();
            // 步骤：先设置关闭标志再 TryComplete，保证 OnSensorStateChanged 能区分"关闭"与"满载"两种 TryWrite 失败原因。
            Volatile.Write(ref _sensorEventChannelCompleted, true);
            _sensorEventChannel.Writer.TryComplete();
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
            ClearRuntimeQueuesForNonRunningState(SystemState.Ready);
        }

        /// <summary>
        /// 刷新分拣时序配置快照。
        /// </summary>
        /// <param name="options">最新分拣时序配置。</param>
        private void RefreshTimingOptionsSnapshot(SortingTaskTimingOptions options) {
            Volatile.Write(ref _timingOptionsSnapshot, options);
        }

        /// <summary>
        /// 释放托管资源与配置热更新订阅。
        /// </summary>
        public override void Dispose() {
            _timingOptionsChangedRegistration.Dispose();
            _parcelSignal.Dispose();
            base.Dispose();
        }

        /// <summary>
        /// 订阅业务事件。
        /// </summary>
        private void SubscribeEvents() {
            _sensorStateChangedHandler ??= OnSensorStateChanged;
            _currentInductionCarrierChangedHandler ??= OnCurrentInductionCarrierChanged;
            _carrierLoadStatusChangedHandler ??= OnCarrierLoadStatusChanged;
            _parcelTargetChuteUpdatedHandler ??= OnParcelTargetChuteUpdated;
            _systemStateChangedHandler ??= OnSystemStateChanged;

            _sensorManager.SensorStateChanged += _sensorStateChangedHandler;
            _carrierManager.CurrentInductionCarrierChanged += _currentInductionCarrierChangedHandler;
            _carrierManager.CarrierLoadStatusChanged += _carrierLoadStatusChangedHandler;
            _parcelManager.ParcelTargetChuteUpdated += _parcelTargetChuteUpdatedHandler;
            _systemStateManager.StateChanged += _systemStateChangedHandler;
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

            if (_systemStateChangedHandler is not null) {
                _systemStateManager.StateChanged -= _systemStateChangedHandler;
            }

            _sensorStateChangedHandler = null;
            _currentInductionCarrierChangedHandler = null;
            _carrierLoadStatusChangedHandler = null;
            _parcelTargetChuteUpdatedHandler = null;
            _systemStateChangedHandler = null;
        }

        /// <summary>
        /// 将原始包裹队列中的包裹按成熟时间转移到待装车队列。
        /// </summary>
        private async Task PumpRawQueueAsync(CancellationToken stoppingToken) {
            while (!stoppingToken.IsCancellationRequested) {
                await WaitForPumpSignalAsync(stoppingToken).ConfigureAwait(false);
                if (_systemStateManager.CurrentState != SystemState.Running) {
                    continue;
                }

                var safeParcelMatureDelayMs = ConfigurationValueHelper.GetPositiveOrDefault(
                    CurrentTimingOptions.ParcelMatureDelayMs,
                    SortingTaskTimingOptions.DefaultParcelMatureDelayMs);

                // 步骤：严格按流水线顺序处理，仅允许队头包裹成熟后再推进后续包裹。
                while (_rawParcelQueue.TryPeek(out var headParcel)) {
                    if (_systemStateManager.CurrentState != SystemState.Running) {
                        break;
                    }

                    if (!TryGetOrCreateParcelMatureStartAt(headParcel.ParcelId, out var matureStartAt)) {
                        var isLostParcel = _lostParcelIdSet.TryRemove(headParcel.ParcelId, out _);
                        if (isLostParcel &&
                            _rawParcelQueue.TryDequeue(out var lostParcel)) {
                            Interlocked.Decrement(ref _rawQueueCount);
                            _carrierLoadingService.UpdateRawQueueCountSnapshot(Volatile.Read(ref _rawQueueCount));
                            _parcelMatureStartAtMap.TryRemove(lostParcel.ParcelId, out _);
                            _logger.LogWarning(
                                "包裹判定丢失，已从原始队列移除且不上车 ParcelId={ParcelId}",
                                lostParcel.ParcelId);
                            continue;
                        }

                        if (isLostParcel) {
                            _logger.LogWarning(
                                "包裹已判定丢失但移除队头失败，等待下一轮重试 ParcelId={ParcelId}",
                                headParcel.ParcelId);
                        }

                        break;
                    }

                    var matureAt = matureStartAt + TimeSpan.FromMilliseconds(safeParcelMatureDelayMs);
                    var delay = matureAt - DateTime.Now;
                    if (delay > TimeSpan.Zero) {
                        await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                        if (_systemStateManager.CurrentState != SystemState.Running) {
                            break;
                        }
                    }

                    if (_rawParcelQueue.TryDequeue(out var readyParcel)) {
                        // 步骤：包裹进入待装车队列后，不再依赖成熟起始时间映射，及时释放内存占用。
                        Interlocked.Decrement(ref _rawQueueCount);
                        _carrierLoadingService.UpdateRawQueueCountSnapshot(Volatile.Read(ref _rawQueueCount));
                        _parcelMatureStartAtMap.TryRemove(readyParcel.ParcelId, out _);
                        _carrierLoadingService.EnqueueReadyParcel(readyParcel);
                        var rawQueueCount = _carrierLoadingService.RawQueueCountSnapshot;
                        var readyQueueCount = _carrierLoadingService.ReadyQueueCount;
                        var inFlightCarrierParcelCount = _carrierLoadingService.InFlightCarrierParcelCount;
                        var densityBucket = _carrierLoadingService.GetDensityBucketLabel(rawQueueCount, readyQueueCount, inFlightCarrierParcelCount);
                        _logger.LogInformation(
                            "包裹进入待装车队列 ParcelId={ParcelId} ReadyQueueCount={ReadyQueueCount} RawQueueCount={RawQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                            readyParcel.ParcelId,
                            readyQueueCount,
                            rawQueueCount,
                            inFlightCarrierParcelCount,
                            densityBucket);
                    }
                }
            }
        }

        /// <summary>
        /// 等待成熟泵送信号（支持按头包裹成熟时间定时唤醒）。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task WaitForPumpSignalAsync(CancellationToken stoppingToken) {
            // 步骤0：非运行态仅阻塞等待状态切换信号，不推进成熟队列。
            if (_systemStateManager.CurrentState != SystemState.Running) {
                await _parcelSignal.WaitAsync(stoppingToken).ConfigureAwait(false);
                return;
            }

            // 步骤1：无待处理包裹时，阻塞等待新信号。
            if (!_rawParcelQueue.TryPeek(out var headParcel)) {
                await _parcelSignal.WaitAsync(stoppingToken).ConfigureAwait(false);
                return;
            }

            // 步骤2：队头包裹未绑定成熟起始时间时，仅等待新事件信号（创建/上车/状态切换）。
            if (!TryGetOrCreateParcelMatureStartAt(headParcel.ParcelId, out var matureStartAt)) {
                await _parcelSignal.WaitAsync(stoppingToken).ConfigureAwait(false);
                return;
            }

            var safeParcelMatureDelayMs = ConfigurationValueHelper.GetPositiveOrDefault(
                CurrentTimingOptions.ParcelMatureDelayMs,
                SortingTaskTimingOptions.DefaultParcelMatureDelayMs);
            var matureAt = matureStartAt + TimeSpan.FromMilliseconds(safeParcelMatureDelayMs);
            var nextWakeDelay = matureAt - DateTime.Now;
            // 步骤3：队头包裹已成熟时立即继续；否则按队头成熟时间等待或被新信号中断。
            if (nextWakeDelay <= TimeSpan.Zero) {
                return;
            }

            _ = await _parcelSignal.WaitAsync(nextWakeDelay, stoppingToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 按 FIFO 顺序消费传感器事件有序通道。
        /// 确保创建包裹触发与上车触发两类传感器事件按物理到达顺序串行处理，
        /// 消除高频密集场景下线程池调度导致的事件相对乱序问题（Phase 3.2 事件顺序稳定化）。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task ConsumeSensorEventChannelAsync(CancellationToken stoppingToken) {
            await foreach (var args in _sensorEventChannel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false)) {
                // 步骤：直接调用传感器事件处理方法，try-catch 隔离单条事件异常，避免异常终止消费循环。
                try {
                    await HandleSensorStateChangedAsync(args).ConfigureAwait(false);
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "处理传感器事件时发生异常 Point={Point} SensorType={SensorType}", args.Point, args.SensorType);
                }
            }
        }

        /// <summary>
        /// 处理传感器状态变化事件。
        /// 将事件写入有序通道，由 <see cref="ConsumeSensorEventChannelAsync"/> 按 FIFO 顺序串行处理，
        /// 保障创建包裹与上车触发事件的处理先后与物理到达顺序一致。
        /// 通道已完成（服务关闭）时降级为 Debug 日志；通道真正满载时对丢弃事件做限频聚合告警，防止日志风暴。
        /// </summary>
        private void OnSensorStateChanged(object? sender, Core.Events.Io.SensorStateChangedEventArgs args) {
            if (_sensorEventChannel.Writer.TryWrite(args)) {
                return;
            }

            // 步骤1：服务关闭流程中 TryWrite 失败属正常现象，降级为 Debug 日志避免误告警。
            if (Volatile.Read(ref _sensorEventChannelCompleted)) {
                _logger.LogDebug(
                    "传感器事件通道已关闭，忽略事件 Point={Point} SensorType={SensorType}",
                    args.Point,
                    args.SensorType);
                return;
            }

            // 步骤2：通道真正满载时递增丢弃计数，并按每秒最多一次的限频策略输出聚合告警，防止高频日志风暴。
            var dropped = Interlocked.Increment(ref _droppedSensorEventCount);
            var nowMs = Environment.TickCount64;
            var lastMs = Volatile.Read(ref _lastDropWarningElapsedMs);
            // 步骤3：使用 unchecked 确保 TickCount64 在极端情况下回绕时差值计算仍然正确。
            if (unchecked(nowMs - lastMs) >= 1000 &&
                Interlocked.CompareExchange(ref _lastDropWarningElapsedMs, nowMs, lastMs) == lastMs) {
                _logger.LogWarning(
                    "传感器事件通道持续满载，已聚合丢弃 DroppedCount={DroppedCount} Point={Point} SensorName={SensorName} SensorType={SensorType} OccurredAtMs={OccurredAtMs}",
                    dropped,
                    args.Point,
                    args.SensorName,
                    args.SensorType,
                    args.OccurredAtMs);
            }
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
        /// 处理系统状态变化事件。
        /// </summary>
        /// <param name="sender">事件发送方。</param>
        /// <param name="args">状态变化事件参数。</param>
        private void OnSystemStateChanged(object? sender, Core.Events.System.StateChangeEventArgs args) {
            _ = _safeExecutor.ExecuteAsync(
                () => HandleSystemStateChangedAsync(args),
                "SortingTaskOrchestrationService.OnSystemStateChanged");
        }

        /// <summary>
        /// 安全执行系统状态变化业务逻辑。
        /// </summary>
        /// <param name="args">状态变化事件参数。</param>
        /// <returns>异步任务。</returns>
        private Task HandleSystemStateChangedAsync(Core.Events.System.StateChangeEventArgs args) {
            if (args.NewState == SystemState.Running) {
                _parcelSignal.Release();
                return Task.CompletedTask;
            }

            ClearRuntimeQueuesForNonRunningState(args.NewState);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 安全执行传感器状态变化业务逻辑。
        /// </summary>
        private async Task HandleSensorStateChangedAsync(Core.Events.Io.SensorStateChangedEventArgs args) {
            // 步骤1：仅处理达到触发电平的事件，避免同一点位抖动导致重复业务执行。
            if (args.NewState != args.TriggerState) {
                return;
            }

            // 步骤2：仅在系统运行态处理分拣业务事件，非运行态直接丢弃。
            if (_systemStateManager.CurrentState != SystemState.Running) {
                return;
            }

            // 步骤3：上车触发源仅用于记录可消费触发时间队列，不直接创建包裹。
            if (args.SensorType == IoPointType.LoadingTriggerSensor) {
                UpdateLoadingTriggerOccurredAt(args.OccurredAtMs);
                return;
            }

            // 步骤4：仅创建包裹触发源负责创建包裹，其余传感器类型忽略。
            if (args.SensorType != IoPointType.ParcelCreateSensor) {
                return;
            }

            // 步骤5：生成包裹编号并创建包裹实体，成熟起始时间在后续泵送阶段按配置解析。
            var parcelId = GenerateParcelIdTicksFromSensorEvent(args.OccurredAtMs);
            var parcel = new ParcelInfo {
                ParcelId = parcelId,
            };

            var created = await _parcelManager.CreateAsync(parcel).ConfigureAwait(false);
            if (!created) {
                _logger.LogWarning("创建包裹失败或包裹已存在 ParcelId={ParcelId}", parcelId);
                return;
            }

            if (GetParcelMatureStartSource() == ParcelMatureStartSource.ParcelCreateSensor) {
                // 步骤6：创建触发源模式可直接确定成熟起始时间，减少后续泵送阶段重复解析。
                _parcelMatureStartAtMap[parcelId] = new DateTime(parcelId, DateTimeKind.Local);
            }
            else {
                _pendingLoadingTriggerParcelIdQueue.Enqueue(parcelId);
                _waitingLoadingTriggerParcelSet[parcelId] = 0;
                Interlocked.Increment(ref _waitingLoadingTriggerParcelCount);
            }
            _rawParcelQueue.Enqueue(parcel);
            var rawQueueCount = Interlocked.Increment(ref _rawQueueCount);
            _carrierLoadingService.UpdateRawQueueCountSnapshot(rawQueueCount);
            _parcelSignal.Release();
            var waitingTriggerCount = Volatile.Read(ref _waitingLoadingTriggerParcelCount);
            _logger.LogInformation(
                "创建包裹成功并入原始队列 ParcelId={ParcelId} WaitingLoadingTriggerParcelCount={WaitingLoadingTriggerParcelCount}",
                parcelId,
                waitingTriggerCount);
        }

        /// <summary>
        /// 获取生效的包裹成熟起始来源。
        /// </summary>
        /// <returns>包裹成熟起始来源。</returns>
        private ParcelMatureStartSource GetParcelMatureStartSource() {
            var timingOptions = CurrentTimingOptions;
            var configuredStartSource = timingOptions.ParcelMatureStartSource;
            return configuredStartSource switch {
                ParcelMatureStartSource.ParcelCreateSensor => ParcelMatureStartSource.ParcelCreateSensor,
                ParcelMatureStartSource.LoadingTriggerSensor => ParcelMatureStartSource.LoadingTriggerSensor,
                _ => SortingTaskTimingOptions.DefaultParcelMatureStartSource
            };
        }

        /// <summary>
        /// 获取或创建包裹成熟时间起始点。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="matureStartAt">成熟起始时间。</param>
        /// <returns>是否已得到可用的成熟起始时间。</returns>
        private bool TryGetOrCreateParcelMatureStartAt(long parcelId, out DateTime matureStartAt) {
            // 步骤1：命中缓存映射时直接返回，避免重复解析。
            if (_parcelMatureStartAtMap.TryGetValue(parcelId, out matureStartAt)) {
                return true;
            }

            var timingOptions = CurrentTimingOptions;
            var startSource = GetParcelMatureStartSource();
            var parcelCreatedAt = new DateTime(parcelId, DateTimeKind.Local);

            // 步骤2：创建触发源模式直接使用包裹创建时间作为成熟起始时间。
            if (startSource == ParcelMatureStartSource.ParcelCreateSensor) {
                matureStartAt = parcelCreatedAt;
                _parcelMatureStartAtMap[parcelId] = matureStartAt;
                return true;
            }

            // 步骤3：上车触发源模式且包裹不在等待集合时，说明仍无法确定成熟起始时间。
            if (!_waitingLoadingTriggerParcelSet.ContainsKey(parcelId)) {
                matureStartAt = default;
                return false;
            }

            // 步骤4：启用回退时按创建时间兜底，并释放等待集合占位。
            if (timingOptions.EnableFallbackToParcelCreateWhenLoadingTriggerMissing) {
                _logger.LogWarning(
                    "上车触发源缺失，按配置回退创建包裹触发源，ParcelId={ParcelId}",
                    parcelId);
                if (_waitingLoadingTriggerParcelSet.TryRemove(parcelId, out _)) {
                    Interlocked.Decrement(ref _waitingLoadingTriggerParcelCount);
                }
                matureStartAt = parcelCreatedAt;
                _parcelMatureStartAtMap[parcelId] = matureStartAt;
                return true;
            }

            // 步骤5：未启用回退时保持等待，等待后续上车触发绑定。
            _logger.LogDebug(
                "上车触发源缺失且未启用回退，头包裹继续等待上车触发绑定 ParcelId={ParcelId}",
                parcelId);
            matureStartAt = default;
            return false;
        }

        /// <summary>
        /// 非运行态时清空运行期队列与映射，避免旧数据污染后续运行周期。
        /// </summary>
        /// <param name="newState">变更后的系统状态。</param>
        private void ClearRuntimeQueuesForNonRunningState(SystemState newState) {
            // 步骤1：清空原始包裹队列。
            var rawParcelClearedCount = ClearQueueAndCountItems(_rawParcelQueue);
            Interlocked.Exchange(ref _rawQueueCount, 0);
            _carrierLoadingService.UpdateRawQueueCountSnapshot(0);

            // 步骤2：清空待绑定上车触发包裹队列与集合。
            var pendingParcelClearedCount = ClearQueueAndCountItems(_pendingLoadingTriggerParcelIdQueue);
            var waitingParcelSetCount = ClearDictionaryAndCountItems(_waitingLoadingTriggerParcelSet);
            Interlocked.Exchange(ref _waitingLoadingTriggerParcelCount, 0);
            var lostParcelSetCount = ClearDictionaryAndCountItems(_lostParcelIdSet);

            // 步骤3：清空成熟起始时间映射与链路时间节点，释放残留状态。
            var matureStartMapCount = _parcelMatureStartAtMap.Count;
            _parcelMatureStartAtMap.Clear();
            _carrierLoadingService.ClearReadyQueue();
            _carrierLoadingService.ClearAllParcelTimelines();

            // 步骤4：释放泵送信号，确保等待中的泵送循环及时感知状态变更。
            _parcelSignal.Release();

            _logger.LogInformation(
                "系统切换为非运行态，已清空分拣运行期队列 NewState={NewState} RawParcelClearedCount={RawParcelClearedCount} PendingParcelClearedCount={PendingParcelClearedCount} WaitingParcelSetCount={WaitingParcelSetCount} LostParcelSetCount={LostParcelSetCount} MatureStartMapCount={MatureStartMapCount}",
                newState,
                rawParcelClearedCount,
                pendingParcelClearedCount,
                waitingParcelSetCount,
                lostParcelSetCount,
                matureStartMapCount);
        }

        /// <summary>
        /// 更新最近一次上车触发源触发时间。
        /// 若当前无可绑定包裹（包裹必须先于触发创建），触发直接丢弃，符合"先有包裹才有触发"的系统原则。
        /// </summary>
        /// <param name="occurredAtMs">传感器触发时间毫秒值。</param>
        private void UpdateLoadingTriggerOccurredAt(long occurredAtMs) {
            // 步骤1：将触发毫秒时间解析为本地时间语义，统一后续绑定与日志口径。
            var loadingTriggerOccurredAt = ResolveLocalDateTimeFromSensorOccurredAtMs(occurredAtMs, "上车触发源");
            var waitingCount = Volatile.Read(ref _waitingLoadingTriggerParcelCount);
            if (!TryBindLoadingTriggerOccurredAt(loadingTriggerOccurredAt, out var boundParcelId)) {
                // 步骤2：无可绑定包裹时直接丢弃该触发，附带队列快照与密度分桶供高密度误差归因。
                var rawQueueCount = _carrierLoadingService.RawQueueCountSnapshot;
                var readyQueueCount = _carrierLoadingService.ReadyQueueCount;
                var inFlightCarrierParcelCount = _carrierLoadingService.InFlightCarrierParcelCount;
                var densityBucket = _carrierLoadingService.GetDensityBucketLabel(rawQueueCount, readyQueueCount, inFlightCarrierParcelCount);
                _logger.LogWarning(
                    "收到上车触发但暂无可绑定包裹，按策略直接丢弃该触发（创建包裹必须先于上车触发） LoadingTriggerOccurredAt={LoadingTriggerOccurredAt:O} WaitingLoadingTriggerParcelCount={WaitingLoadingTriggerParcelCount} RawQueueCount={RawQueueCount} ReadyQueueCount={ReadyQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                    loadingTriggerOccurredAt,
                    waitingCount,
                    rawQueueCount,
                    readyQueueCount,
                    inFlightCarrierParcelCount,
                    densityBucket);
            }
            else {
                // 步骤3：绑定成功后记录链路耗时与统一队列快照，保证观测字段稳定一致。
                _carrierLoadingService.RecordLoadingTriggerBoundAt(boundParcelId, loadingTriggerOccurredAt);
                var hasElapsedFromCreated = _carrierLoadingService.TryGetElapsedFromCreatedToLoadingTrigger(boundParcelId, out var elapsedFromCreated);
                var rawQueueCount = _carrierLoadingService.RawQueueCountSnapshot;
                var readyQueueCount = _carrierLoadingService.ReadyQueueCount;
                var inFlightCarrierParcelCount = _carrierLoadingService.InFlightCarrierParcelCount;
                var densityBucket = _carrierLoadingService.GetDensityBucketLabel(rawQueueCount, readyQueueCount, inFlightCarrierParcelCount);
                var remainingWaitingCount = Volatile.Read(ref _waitingLoadingTriggerParcelCount);
                if (hasElapsedFromCreated) {
                    // 步骤4：绑定成功且获得创建→上车触发耗时，直接通过数值方法记录统计样本（避免字符串解析）。
                    if (_carrierLoadingService.TryGetCreatedToLoadingTriggerElapsedMs(boundParcelId, out var createdToTriggerMs)) {
                        _carrierLoadingService.RecordCreatedToLoadingTriggerElapsed(createdToTriggerMs, densityBucket);
                        // 步骤5：超阈值时计入超阈值率并输出告警，保证全四段链路均有精度看板指标。
                        var alertThresholdMs = ConfigurationValueHelper.GetPositiveOrDefault(
                            CurrentTimingOptions.ParcelChainAlertThresholdMs,
                            SortingTaskTimingOptions.DefaultParcelChainAlertThresholdMs);
                        if (createdToTriggerMs > alertThresholdMs) {
                            _carrierLoadingService.RecordCreatedToLoadingTriggerExceedance(densityBucket);
                            _logger.LogWarning(
                                "创建包裹到上车触发链路耗时超阈值告警 ParcelId={ParcelId} LoadingTriggerOccurredAt={LoadingTriggerOccurredAt:O} ElapsedMs={ElapsedMs} ThresholdMs={ThresholdMs} WaitingLoadingTriggerParcelCount={WaitingLoadingTriggerParcelCount} RawQueueCount={RawQueueCount} ReadyQueueCount={ReadyQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                                boundParcelId,
                                loadingTriggerOccurredAt,
                                createdToTriggerMs,
                                alertThresholdMs,
                                remainingWaitingCount,
                                rawQueueCount,
                                readyQueueCount,
                                inFlightCarrierParcelCount,
                                densityBucket);
                        }
                    }

                    _logger.LogInformation(
                        "上车触发已绑定包裹 ParcelId={ParcelId} LoadingTriggerOccurredAt={LoadingTriggerOccurredAt:O} [距离创建包裹:{ElapsedFromCreated}] WaitingLoadingTriggerParcelCount={WaitingLoadingTriggerParcelCount} RawQueueCount={RawQueueCount} ReadyQueueCount={ReadyQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                        boundParcelId,
                        loadingTriggerOccurredAt,
                        elapsedFromCreated,
                        remainingWaitingCount,
                        rawQueueCount,
                        readyQueueCount,
                        inFlightCarrierParcelCount,
                        densityBucket);
                }
                else {
                    _logger.LogInformation(
                        "上车触发已绑定包裹 ParcelId={ParcelId} LoadingTriggerOccurredAt={LoadingTriggerOccurredAt:O} WaitingLoadingTriggerParcelCount={WaitingLoadingTriggerParcelCount} RawQueueCount={RawQueueCount} ReadyQueueCount={ReadyQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                        boundParcelId,
                        loadingTriggerOccurredAt,
                        remainingWaitingCount,
                        rawQueueCount,
                        readyQueueCount,
                        inFlightCarrierParcelCount,
                        densityBucket);
                }
            }
            _parcelSignal.Release();

            _logger.LogDebug(
                "更新上车触发源时间 LoadingTriggerOccurredAt={LoadingTriggerOccurredAt:O}",
                loadingTriggerOccurredAt);
        }

        /// <summary>
        /// 尝试将上车触发时间绑定到最早待绑定包裹（FIFO）。
        /// </summary>
        /// <param name="loadingTriggerOccurredAt">上车触发时间。</param>
        /// <param name="boundParcelId">绑定成功的包裹编号。</param>
        /// <returns>是否绑定成功。</returns>
        private bool TryBindLoadingTriggerOccurredAt(DateTime loadingTriggerOccurredAt, out long boundParcelId) {
            var lagWindowMs = ConfigurationValueHelper.GetPositiveOrDefault(
                CurrentTimingOptions.LoadingTriggerLagWindowMs,
                SortingTaskTimingOptions.DefaultLoadingTriggerLagWindowMs);
            var lagWindow = TimeSpan.FromMilliseconds(lagWindowMs);

            // 步骤1：按包裹 FIFO 顺序消耗待绑定队列，保证上车触发绑定顺序与流水线顺序一致。
            while (_pendingLoadingTriggerParcelIdQueue.TryDequeue(out var candidateParcelId)) {
                if (!_waitingLoadingTriggerParcelSet.TryRemove(candidateParcelId, out _)) {
                    continue;
                }

                // TryRemove 成功即代表该包裹从待触发集合中消耗，同步递减计数。
                Interlocked.Decrement(ref _waitingLoadingTriggerParcelCount);

                // 步骤2：滞后超窗时将包裹标记为丢失，并继续尝试将同一触发绑定到后续包裹。
                var parcelCreatedAt = new DateTime(candidateParcelId, DateTimeKind.Local);
                var lag = loadingTriggerOccurredAt - parcelCreatedAt;
                if (lag > lagWindow) {
                    _lostParcelIdSet[candidateParcelId] = 0;
                    _logger.LogWarning(
                        "包裹上车触发滞后超窗，判定丢失并跳过上车 ParcelId={ParcelId} ParcelCreatedAt={ParcelCreatedAt:O} LoadingTriggerOccurredAt={LoadingTriggerOccurredAt:O} LagMs={LagMs} LoadingTriggerLagWindowMs={LoadingTriggerLagWindowMs}",
                        candidateParcelId,
                        parcelCreatedAt,
                        loadingTriggerOccurredAt,
                        lag.TotalMilliseconds,
                        lagWindowMs);
                    continue;
                }

                // 步骤3：命中可绑定包裹后记录成熟起点并返回成功。
                _parcelMatureStartAtMap[candidateParcelId] = loadingTriggerOccurredAt;
                boundParcelId = candidateParcelId;
                return true;
            }

            // 步骤4：无可绑定包裹时按"先有包裹才有触发"原则直接丢弃该触发，返回失败；禁止缓存触发以回放。
            boundParcelId = default;
            return false;
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

        /// <summary>
        /// 清空并统计并发队列中的元素数量。
        /// </summary>
        /// <typeparam name="T">队列元素类型。</typeparam>
        /// <param name="queue">目标并发队列。</param>
        /// <returns>清空的元素数量。</returns>
        private static int ClearQueueAndCountItems<T>(ConcurrentQueue<T> queue) {
            var count = 0;
            while (queue.TryDequeue(out _)) {
                count++;
            }

            return count;
        }

        /// <summary>
        /// 清空并统计并发字典中的元素数量。
        /// </summary>
        /// <typeparam name="TKey">键类型。</typeparam>
        /// <typeparam name="TValue">值类型。</typeparam>
        /// <param name="dictionary">目标并发字典。</param>
        /// <returns>清空的元素数量。</returns>
        private static int ClearDictionaryAndCountItems<TKey, TValue>(ConcurrentDictionary<TKey, TValue> dictionary)
            where TKey : notnull {
            var count = 0;
            foreach (var key in dictionary.Keys) {
                if (dictionary.TryRemove(key, out _)) {
                    count++;
                }
            }

            return count;
        }

    }
}
