using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;
using Zeye.NarrowBeltSorter.Core.Options.Sorting;
using Zeye.NarrowBeltSorter.Core.Utilities;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 分拣任务落格编排服务：负责小车到位映射、命中目标格口后的落格执行与解绑。
    /// </summary>
    public sealed class SortingTaskDropOrchestrationService : IDisposable {
        /// <summary>
        /// 落格命令有界通道容量（条）。
        /// </summary>
        private const int DropCommandChannelCapacity = 2048;

        private readonly ILogger<SortingTaskDropOrchestrationService> _logger;
        private readonly ICarrierManager _carrierManager;
        private readonly IParcelManager _parcelManager;
        private readonly IChuteManager _chuteManager;
        private readonly SortingTaskCarrierLoadingService _carrierLoadingService;
        private readonly IOptionsMonitor<SortingTaskTimingOptions> _sortingTaskTimingOptionsMonitor;
        private readonly IDisposable _timingOptionsChangedRegistration;
        private SortingTaskTimingOptions _timingOptionsSnapshot;

        /// <summary>
        /// 小车是否到达目标格口的状态缓存（用于错过格口判定）。
        /// </summary>
        private readonly ConcurrentDictionary<long, bool> _carrierAtTargetStates = [];

        /// <summary>
        /// 小车索引缓存锁对象。
        /// </summary>
        private readonly object _carrierIndexCacheLock = new();

        /// <summary>
        /// 小车编号到索引的缓存映射。
        /// </summary>
        private IReadOnlyDictionary<long, int> _cachedCarrierIndexMap = new Dictionary<long, int>();

        /// <summary>
        /// 有序小车编号缓存（与索引缓存同步维护）。
        /// </summary>
        private long[] _cachedOrderedCarrierIds = [];

        /// <summary>
        /// 小车索引缓存签名。
        /// </summary>
        private int _cachedCarrierIndexSignature;

        /// <summary>
        /// 小车索引缓存数量。
        /// </summary>
        private int _cachedCarrierIndexCount;

        /// <summary>
        /// 落格命令有序通道（单消费者），用于异步分流落格慢动作。
        /// </summary>
        private readonly Channel<DropCommand> _dropCommandChannel =
            Channel.CreateBounded<DropCommand>(
                new BoundedChannelOptions(DropCommandChannelCapacity) {
                    FullMode = BoundedChannelFullMode.DropWrite,
                    SingleReader = true,
                    SingleWriter = false
                });

        /// <summary>
        /// 落格命令通道关闭标志。
        /// </summary>
        private bool _dropCommandChannelCompleted;

        /// <summary>
        /// 落格命令通道累计丢弃数。
        /// </summary>
        private long _droppedDropCommandCount;

        /// <summary>
        /// 落格命令通道最近一次丢弃告警时间刻（毫秒）。
        /// </summary>
        private long _lastDropCommandWarningElapsedMs;

        /// <summary>
        /// 落格命令消费者取消源。
        /// </summary>
        private readonly CancellationTokenSource _dropCommandConsumerCts = new();

        /// <summary>
        /// 落格命令消费者任务。
        /// </summary>
        private Task? _dropCommandConsumerTask;

        /// <summary>
        /// 落格命令消费者启动标志（0=未启动，1=已启动）。
        /// </summary>
        private int _dropCommandConsumerStarted;

        /// <summary>
        /// 落格命令按小车去重集合。
        /// </summary>
        private readonly ConcurrentDictionary<long, byte> _dropCommandCarrierSet = new();

        /// <summary>
        /// 落格命令按包裹去重集合。
        /// </summary>
        private readonly ConcurrentDictionary<long, byte> _dropCommandParcelSet = new();

        /// <summary>
        /// 落格命令按“小车+格口”去重集合。
        /// </summary>
        private readonly ConcurrentDictionary<CarrierChuteCommandKey, byte> _dropCommandCarrierChuteSet = new();

        /// <summary>
        /// 初始化落格编排服务。
        /// </summary>
        public SortingTaskDropOrchestrationService(
            ILogger<SortingTaskDropOrchestrationService> logger,
            ICarrierManager carrierManager,
            IParcelManager parcelManager,
            IChuteManager chuteManager,
            SortingTaskCarrierLoadingService carrierLoadingService,
            IOptionsMonitor<SortingTaskTimingOptions> sortingTaskTimingOptionsMonitor) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _carrierManager = carrierManager ?? throw new ArgumentNullException(nameof(carrierManager));
            _parcelManager = parcelManager ?? throw new ArgumentNullException(nameof(parcelManager));
            _chuteManager = chuteManager ?? throw new ArgumentNullException(nameof(chuteManager));
            _carrierLoadingService = carrierLoadingService ?? throw new ArgumentNullException(nameof(carrierLoadingService));
            _sortingTaskTimingOptionsMonitor = sortingTaskTimingOptionsMonitor ?? throw new ArgumentNullException(nameof(sortingTaskTimingOptionsMonitor));
            _timingOptionsSnapshot = _sortingTaskTimingOptionsMonitor.CurrentValue ?? throw new InvalidOperationException("SortingTaskTimingOptions 不能为空。");
            _timingOptionsChangedRegistration = _sortingTaskTimingOptionsMonitor.OnChange(RefreshTimingOptionsSnapshot) ?? throw new InvalidOperationException("SortingTaskTimingOptions.OnChange 订阅失败。");
        }

        /// <summary>
        /// 落格命令去重键。
        /// </summary>
        /// <param name="CarrierId">小车编号。</param>
        /// <param name="ChuteId">格口编号。</param>
        private readonly record struct CarrierChuteCommandKey(long CarrierId, long ChuteId);

        /// <summary>
        /// 落格命令对象。
        /// </summary>
        /// <param name="CurrentInductionCarrierId">当前感应位小车编号。</param>
        /// <param name="CarrierId">目标小车编号。</param>
        /// <param name="ParcelId">包裹编号。</param>
        /// <param name="ChuteId">目标格口编号。</param>
        /// <param name="ChangedAt">事件发生时间。</param>
        /// <param name="IsForcedChutePass">是否为强排经过命令。</param>
        private readonly record struct DropCommand(
            long CurrentInductionCarrierId,
            long CarrierId,
            long ParcelId,
            long ChuteId,
            DateTime ChangedAt,
            bool IsForcedChutePass);

        /// <summary>
        /// 启动落格命令消费者。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        public Task StartAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _dropCommandConsumerStarted, 1) == 1) {
                return Task.CompletedTask;
            }

            _dropCommandConsumerTask = Task.Run(
                () => ConsumeDropCommandChannelAsync(_dropCommandConsumerCts.Token),
                _dropCommandConsumerCts.Token);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 停止落格命令消费者并关闭通道。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        public async Task StopAsync(CancellationToken cancellationToken = default) {
            Volatile.Write(ref _dropCommandChannelCompleted, true);
            _dropCommandChannel.Writer.TryComplete();
            _dropCommandConsumerCts.Cancel();
            if (_dropCommandConsumerTask is null) {
                return;
            }

            try {
                await _dropCommandConsumerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // 正常停止路径。
            }
            finally {
                _dropCommandCarrierSet.Clear();
                _dropCommandParcelSet.Clear();
                _dropCommandCarrierChuteSet.Clear();
            }
        }

        /// <summary>
        /// 当前分拣时序配置快照。
        /// </summary>
        private SortingTaskTimingOptions CurrentTimingOptions => Volatile.Read(ref _timingOptionsSnapshot);

        /// <summary>
        /// 处理当前感应位小车变化业务逻辑。
        /// </summary>
        public async ValueTask HandleCurrentInductionCarrierChangedAsync(
            Core.Events.Carrier.CurrentInductionCarrierChangedEventArgs args,
            SystemState currentState,
            CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            if (currentState != SystemState.Running || !args.NewCarrierId.HasValue) {
                return;
            }

            await _carrierLoadingService.TryLoadParcelAtLoadingZoneAsync(
                args.NewCarrierId.Value,
                args.ChangedAt,
                cancellationToken).ConfigureAwait(false);

            // 步骤1：无在途绑定包裹时快速返回，避免 20ms 高频事件进入落格扫描热路径。
            if (!_carrierLoadingService.HasCarrierParcelMapping) {
                return;
            }

            var orderedCarrierIds = GetOrderedCarrierIds();
            if (orderedCarrierIds.Length == 0) {
                foreach (var mapping in _carrierLoadingService.CarrierParcelMap) {
                    var barCode = ResolveParcelBarCode(mapping.Value);
                    _logger.LogWarning(
                        "落格跳过 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} 原因=环形小车未构建或小车列表为空",
                        mapping.Value,
                        barCode,
                        mapping.Key);
                }

                return;
            }

            // 步骤2：预先构建索引映射，供热路径各扫描分支共享，避免重复构建与 O(n) 线性扫描。
            var carrierIndexMap = GetOrBuildCarrierIndexMap(orderedCarrierIds);
            // 步骤3： 始终执行靠近格口事件检测，确保事件语义不依赖日志级别。
            await DetectApproachingTargetChute(
                args.NewCarrierId.Value,
                args.ChangedAt,
                orderedCarrierIds,
                carrierIndexMap).ConfigureAwait(false);
            await HandleForcedChutePassAsync(args.NewCarrierId.Value, args.ChangedAt, orderedCarrierIds, carrierIndexMap, cancellationToken).ConfigureAwait(false);

            foreach (var pair in _carrierManager.ChuteCarrierOffsetMap) {
                var chuteId = pair.Key;
                var chuteOffset = pair.Value;
                var carrierIdAtChute = ResolveCarrierIdAtChute(args.NewCarrierId.Value, chuteOffset, orderedCarrierIds, carrierIndexMap);
                if (!carrierIdAtChute.HasValue) {
                    continue;
                }

                if (!_carrierLoadingService.TryGetParcelId(carrierIdAtChute.Value, out var parcelId)) {
                    continue;
                }

                if (!_parcelManager.TryGet(parcelId, out var parcel)) {
                    var barCode = ResolveParcelBarCode(parcelId);
                    _logger.LogWarning(
                        "落格跳过 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ChuteId={ChuteId} 原因=包裹快照不存在",
                        parcelId,
                        barCode,
                        carrierIdAtChute.Value,
                        chuteId);
                    continue;
                }

                if (parcel.TargetChuteId != chuteId) {
                    _logger.LogDebug(
                        "落格跳过 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} CurrentChuteId={CurrentChuteId} TargetChuteId={TargetChuteId} 原因=未到目标格口",
                        parcelId,
                        parcel.BarCode,
                        carrierIdAtChute.Value,
                        chuteId,
                        parcel.TargetChuteId);
                    continue;
                }

                _carrierLoadingService.RecordArrivedTargetChute(
                    parcelId,
                    args.ChangedAt,
                    out var previousNodeName,
                    out var elapsedFromPrevious,
                    out var elapsedFromPreviousMs,
                    out var hasValidPreviousNode);
                var rawQueueCount = _carrierLoadingService.RawQueueCountSnapshot;
                var readyQueueCount = _carrierLoadingService.ReadyQueueCount;
                var inFlightCarrierParcelCount = _carrierLoadingService.InFlightCarrierParcelCount;
                var densityBucket = _carrierLoadingService.GetDensityBucketLabel(rawQueueCount, readyQueueCount, inFlightCarrierParcelCount);
                if (hasValidPreviousNode) {
                    _carrierLoadingService.RecordLoadedToArrivedElapsed(elapsedFromPreviousMs, densityBucket);
                }
                else {
                    _logger.LogDebug(
                        "跳过记录上车到到达格口统计样本 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} TargetChuteId={ChuteId} 原因=上一节点不可判定",
                        parcelId,
                        parcel.BarCode,
                        carrierIdAtChute.Value,
                        chuteId);
                }

                _logger.LogInformation(
                    "小车到达目标格口准备落格 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} TargetChuteId={ChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId} [距离 {PreviousNodeName}: {ElapsedFromPrevious}] RawQueueCount={RawQueueCount} ReadyQueueCount={ReadyQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                    parcelId,
                    parcel.BarCode,
                    carrierIdAtChute.Value,
                    chuteId,
                    args.NewCarrierId.Value,
                    previousNodeName,
                    elapsedFromPrevious,
                    rawQueueCount,
                    readyQueueCount,
                    inFlightCarrierParcelCount,
                    densityBucket);
                var arrivalAlertThresholdMs = ConfigurationValueHelper.GetPositiveOrDefault(
                    CurrentTimingOptions.ParcelChainAlertThresholdMs,
                    SortingTaskTimingOptions.DefaultParcelChainAlertThresholdMs);
                if (hasValidPreviousNode && elapsedFromPreviousMs > arrivalAlertThresholdMs) {
                    _carrierLoadingService.RecordLoadedToArrivedExceedance(densityBucket);
                    _logger.LogWarning(
                        "到达目标格口链路耗时超阈值告警 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} TargetChuteId={ChuteId} ChuteOffset={ChuteOffset} CurrentInductionCarrierId={CurrentInductionCarrierId} PreviousNodeName={PreviousNodeName} ElapsedMs={ElapsedMs} ThresholdMs={ThresholdMs} RawQueueCount={RawQueueCount} ReadyQueueCount={ReadyQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                        parcelId,
                        parcel.BarCode,
                        carrierIdAtChute.Value,
                        chuteId,
                        chuteOffset,
                        args.NewCarrierId.Value,
                        previousNodeName,
                        elapsedFromPreviousMs,
                        arrivalAlertThresholdMs,
                        rawQueueCount,
                        readyQueueCount,
                        inFlightCarrierParcelCount,
                        densityBucket);
                }

                if (!_chuteManager.TryGetChute(chuteId, out _)) {
                    _logger.LogWarning(
                        "落格异常 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ChuteId={ChuteId} 原因=未找到格口",
                        parcelId,
                        parcel.BarCode,
                        carrierIdAtChute.Value,
                        chuteId);
                    continue;
                }

                TryEnqueueDropCommand(
                    new DropCommand(
                        args.NewCarrierId.Value,
                        carrierIdAtChute.Value,
                        parcelId,
                        chuteId,
                        args.ChangedAt,
                        false));
            }

            DetectMissedChute(args.NewCarrierId.Value, orderedCarrierIds, carrierIndexMap);
        }

        /// <summary>
        /// 获取按小车编号升序排列的小车编号数组（复用缓存，避免热路径重复排序与分配）。
        /// </summary>
        private long[] GetOrderedCarrierIds() {
            lock (_carrierIndexCacheLock) {
                // 步骤1：在锁内读取当前小车列表，确保计数与缓存校验原子一致。
                if (!_carrierManager.IsRingBuilt) {
                    return [];
                }

                var carriers = _carrierManager.Carriers;
                var count = carriers.Count;
                if (count == 0) {
                    return [];
                }

                // 步骤2：先收集当前小车编号并排序，再与缓存签名对比，确保编号集合变化时也能失效。
                var sortedIds = new long[count];
                var idx = 0;
                foreach (var carrier in carriers) {
                    sortedIds[idx++] = carrier.Id;
                }

                Array.Sort(sortedIds);
                var signature = ComputeCarrierIndexSignature(sortedIds);

                if (_cachedCarrierIndexCount == count && _cachedCarrierIndexSignature == signature) {
                    return _cachedOrderedCarrierIds;
                }

                // 步骤3：缓存失效，委托共享重建逻辑更新有序编号数组与索引映射。
                RebuildCarrierCacheLocked(sortedIds);
                return _cachedOrderedCarrierIds;
            }
        }

        /// <summary>
        /// 根据当前感应位小车和格口偏移量，利用索引映射以 O(1) 复杂度解析位于目标格口前的小车编号。
        /// 偏移语义与上车位一致：按逆时针方向计算。
        /// </summary>
        /// <param name="currentInductionCarrierId">当前感应位小车编号。</param>
        /// <param name="chuteOffset">格口对应的小车偏移量。</param>
        /// <param name="orderedCarrierIds">环形小车有序编号。</param>
        /// <param name="carrierIndexMap">小车编号到索引的映射（O(1) 查找）。</param>
        /// <returns>目标格口对应的小车编号；无法解析时返回 null。</returns>
        private static long? ResolveCarrierIdAtChute(
            long currentInductionCarrierId,
            int chuteOffset,
            long[] orderedCarrierIds,
            IReadOnlyDictionary<long, int> carrierIndexMap) {
            // 步骤1：利用索引映射以 O(1) 定位当前感应位小车索引，避免 O(n) 线性扫描。
            if (!carrierIndexMap.TryGetValue(currentInductionCarrierId, out var currentIndex)) {
                return null;
            }

            // 步骤2：按逆时针偏移计算目标格口对应小车索引，并映射回有序编号数组。
            var mappedIndex = CircularValueHelper.WrapIndex(currentIndex - chuteOffset, orderedCarrierIds.Length);
            return orderedCarrierIds[mappedIndex];
        }

        /// <summary>
        /// 判断指定小车是否处于其目标格口映射位置。
        /// </summary>
        /// <param name="carrierId">小车编号。</param>
        /// <param name="targetChuteId">目标格口编号。</param>
        /// <param name="currentInductionCarrierId">当前感应位小车编号。</param>
        /// <param name="orderedCarrierIds">环形小车有序编号。</param>
        /// <param name="carrierIndexMap">小车编号到索引的映射（O(1) 查找）。</param>
        /// <returns>是否命中目标格口。</returns>
        private bool IsCarrierAtTargetChute(long carrierId, long targetChuteId, long currentInductionCarrierId, long[] orderedCarrierIds, IReadOnlyDictionary<long, int> carrierIndexMap) {
            if (!_carrierManager.ChuteCarrierOffsetMap.TryGetValue(targetChuteId, out var offset)) {
                return false;
            }

            var mappedCarrierId = ResolveCarrierIdAtChute(currentInductionCarrierId, offset, orderedCarrierIds, carrierIndexMap);
            return mappedCarrierId.HasValue && mappedCarrierId.Value == carrierId;
        }

        /// <summary>
        /// 检测并记录错过目标格口的小车包裹。
        /// </summary>
        /// <param name="currentInductionCarrierId">当前感应位小车编号。</param>
        /// <param name="orderedCarrierIds">环形小车有序编号。</param>
        /// <param name="carrierIndexMap">小车编号到索引的映射（O(1) 查找）。</param>
        private void DetectMissedChute(long currentInductionCarrierId, long[] orderedCarrierIds, IReadOnlyDictionary<long, int> carrierIndexMap) {
            // 步骤1：仅在状态缓存非空时才分配清理列表，避免高频热路径空转时的无效 List 分配。
            if (!_carrierAtTargetStates.IsEmpty) {
                List<long>? staleCarrierIds = null;
                foreach (var stateEntry in _carrierAtTargetStates) {
                    if (!_carrierLoadingService.TryGetParcelId(stateEntry.Key, out _)) {
                        staleCarrierIds ??= [];
                        staleCarrierIds.Add(stateEntry.Key);
                    }
                }

                if (staleCarrierIds is not null) {
                    foreach (var staleCarrierId in staleCarrierIds) {
                        _carrierAtTargetStates.TryRemove(staleCarrierId, out _);
                    }
                }
            }

            foreach (var mapping in _carrierLoadingService.CarrierParcelMap) {
                var carrierId = mapping.Key;
                var parcelId = mapping.Value;
                if (!_parcelManager.TryGet(parcelId, out var parcel) || parcel.TargetChuteId <= 0) {
                    continue;
                }

                var isAtTarget = IsCarrierAtTargetChute(carrierId, parcel.TargetChuteId, currentInductionCarrierId, orderedCarrierIds, carrierIndexMap);
                var wasAtTarget = _carrierAtTargetStates.TryGetValue(carrierId, out var previousAtTarget) && previousAtTarget;
                if (wasAtTarget && !isAtTarget) {
                    _logger.LogWarning(
                        "错过格口 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} TargetChuteId={TargetChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId}",
                        parcelId,
                        parcel.BarCode,
                        carrierId,
                        parcel.TargetChuteId,
                        currentInductionCarrierId);
                }

                _carrierAtTargetStates[carrierId] = isAtTarget;
            }
        }

        /// <summary>
        /// 检测并记录“靠近目标格口”状态：目标距离为 2 个小车。
        /// </summary>
        /// <param name="currentInductionCarrierId">当前感应位小车编号。</param>
        /// <param name="changedAt">当前感应位变化时间。</param>
        /// <param name="orderedCarrierIds">环形小车有序编号。</param>
        /// <param name="carrierIndexMap">小车编号到索引的映射（由调用方预先构建，避免重复构建）。</param>
        private async ValueTask DetectApproachingTargetChute(
            long currentInductionCarrierId,
            DateTime changedAt,
            long[] orderedCarrierIds,
            IReadOnlyDictionary<long, int> carrierIndexMap) {
            // 步骤1：遍历已绑定包裹，定位每个目标格口对应的小车位置。
            if (orderedCarrierIds.Length == 0) {
                return;
            }

            foreach (var mapping in _carrierLoadingService.CarrierParcelMap) {
                var carrierId = mapping.Key;
                var parcelId = mapping.Value;
                if (!_parcelManager.TryGet(parcelId, out var parcel) || parcel.TargetChuteId <= 0) {
                    continue;
                }

                if (!_carrierManager.ChuteCarrierOffsetMap.TryGetValue(parcel.TargetChuteId, out var targetOffset)) {
                    _logger.LogWarning(
                        "靠近目标格口判定失败 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} TargetChuteId={TargetChuteId} 原因=目标格口偏移未配置",
                        parcelId,
                        parcel.BarCode,
                        carrierId,
                        parcel.TargetChuteId);
                    continue;
                }

                var targetCarrierIdAtChute = ResolveCarrierIdAtChute(currentInductionCarrierId, targetOffset, orderedCarrierIds, carrierIndexMap);
                if (!targetCarrierIdAtChute.HasValue) {
                    _logger.LogWarning(
                        "靠近目标格口判定失败 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} TargetChuteId={TargetChuteId} 原因=无法解析目标格口对应小车",
                        parcelId,
                        parcel.BarCode,
                        carrierId,
                        parcel.TargetChuteId);
                    continue;
                }

                // 步骤2：计算环形距离并在距离等于 2 时发布“即将分拣”事件与日志。
                var distanceToTarget = GetCircularDistance(carrierId, targetCarrierIdAtChute.Value, orderedCarrierIds.Length, carrierIndexMap);
                if (distanceToTarget == 2) {
                    await _carrierManager.PublishCarrierApproachingTargetChuteAsync(new Core.Events.Carrier.CarrierApproachingTargetChuteEventArgs {
                        CarrierId = carrierId,
                        ParcelId = parcelId,
                        TargetChuteId = parcel.TargetChuteId,
                        CurrentInductionCarrierId = currentInductionCarrierId,
                        DistanceToTarget = distanceToTarget,
                        OccurredAt = changedAt,
                    }).ConfigureAwait(false);
                    _logger.LogDebug(
                        "小车靠近目标格口即将分拣 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} TargetChuteId={TargetChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId} CurrentTargetCarrierId={TargetCarrierId} DistanceToTarget={DistanceToTarget}",
                        parcelId,
                        parcel.BarCode,
                        carrierId,
                        parcel.TargetChuteId,
                        currentInductionCarrierId,
                        targetCarrierIdAtChute.Value,
                        distanceToTarget);
                }
            }
        }

        /// <summary>
        /// 处理小车经过强排格口事件，并执行卸货与链路清理。
        /// </summary>
        /// <param name="currentInductionCarrierId">当前感应区小车 Id。</param>
        /// <param name="changedAt">事件时间。</param>
        /// <param name="orderedCarrierIds">环形小车有序编号。</param>
        /// <param name="carrierIndexMap">小车索引映射。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private async ValueTask HandleForcedChutePassAsync(
            long currentInductionCarrierId,
            DateTime changedAt,
            long[] orderedCarrierIds,
            IReadOnlyDictionary<long, int> carrierIndexMap,
            CancellationToken cancellationToken) {
            if (!_chuteManager.ForcedChuteId.HasValue || _chuteManager.ForcedChuteId.Value <= 0) {
                return;
            }

            var forcedChuteId = _chuteManager.ForcedChuteId.Value;
            if (!_carrierManager.ChuteCarrierOffsetMap.TryGetValue(forcedChuteId, out var forcedChuteOffset)) {
                return;
            }

            var carrierIdAtForcedChute = ResolveCarrierIdAtChute(currentInductionCarrierId, forcedChuteOffset, orderedCarrierIds, carrierIndexMap);
            if (!carrierIdAtForcedChute.HasValue || !_carrierLoadingService.TryGetParcelId(carrierIdAtForcedChute.Value, out var parcelId)) {
                return;
            }

            var barCode = ResolveParcelBarCode(parcelId);
            await _carrierManager.PublishCarrierPassedForcedChuteAsync(new Core.Events.Carrier.CarrierPassedForcedChuteEventArgs {
                CarrierId = carrierIdAtForcedChute.Value,
                ParcelId = parcelId,
                ForcedChuteId = forcedChuteId,
                CurrentInductionCarrierId = currentInductionCarrierId,
                OccurredAt = changedAt,
            }, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "小车经过强排格口 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ForcedChuteId={ForcedChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId}",
                parcelId,
                barCode,
                carrierIdAtForcedChute.Value,
                forcedChuteId,
                currentInductionCarrierId);

            TryEnqueueDropCommand(
                new DropCommand(
                    currentInductionCarrierId,
                    carrierIdAtForcedChute.Value,
                    parcelId,
                    forcedChuteId,
                    changedAt,
                    true));
        }

        /// <summary>
        /// 写入落格命令通道（含去重与满载聚合告警）。
        /// </summary>
        /// <param name="command">落格命令。</param>
        private void TryEnqueueDropCommand(DropCommand command) {
            var carrierChuteKey = new CarrierChuteCommandKey(command.CarrierId, command.ChuteId);
            if (!_dropCommandCarrierSet.TryAdd(command.CarrierId, 0)) {
                return;
            }

            if (!_dropCommandParcelSet.TryAdd(command.ParcelId, 0)) {
                _dropCommandCarrierSet.TryRemove(command.CarrierId, out _);
                return;
            }

            if (!_dropCommandCarrierChuteSet.TryAdd(carrierChuteKey, 0)) {
                _dropCommandCarrierSet.TryRemove(command.CarrierId, out _);
                _dropCommandParcelSet.TryRemove(command.ParcelId, out _);
                return;
            }

            if (_dropCommandChannel.Writer.TryWrite(command)) {
                return;
            }

            ReleaseDropCommandReservation(command);
            if (Volatile.Read(ref _dropCommandChannelCompleted)) {
                _logger.LogDebug(
                    "落格命令通道已关闭，忽略命令 CarrierId={CarrierId} ParcelId={ParcelId} ChuteId={ChuteId}",
                    command.CarrierId,
                    command.ParcelId,
                    command.ChuteId);
                return;
            }

            var dropped = Interlocked.Increment(ref _droppedDropCommandCount);
            var nowMs = Environment.TickCount64;
            var lastMs = Volatile.Read(ref _lastDropCommandWarningElapsedMs);
            if (unchecked(nowMs - lastMs) >= 1000 &&
                Interlocked.CompareExchange(ref _lastDropCommandWarningElapsedMs, nowMs, lastMs) == lastMs) {
                _logger.LogWarning(
                    "落格命令通道持续满载，已聚合丢弃 DroppedCount={DroppedCount} CarrierId={CarrierId} ParcelId={ParcelId} ChuteId={ChuteId}",
                    dropped,
                    command.CarrierId,
                    command.ParcelId,
                    command.ChuteId);
            }
        }

        /// <summary>
        /// 单线程消费落格命令通道。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task ConsumeDropCommandChannelAsync(CancellationToken stoppingToken) {
            await foreach (var command in _dropCommandChannel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false)) {
                try {
                    await ExecuteDropCommandAsync(command, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    break;
                }
                catch (Exception ex) {
                    _logger.LogError(
                        ex,
                        "执行落格命令异常 CarrierId={CarrierId} ParcelId={ParcelId} ChuteId={ChuteId} IsForced={IsForced}",
                        command.CarrierId,
                        command.ParcelId,
                        command.ChuteId,
                        command.IsForcedChutePass);
                }
                finally {
                    ReleaseDropCommandReservation(command);
                }
            }
        }

        /// <summary>
        /// 执行落格命令慢动作并记录统计日志。
        /// </summary>
        /// <param name="command">落格命令。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task ExecuteDropCommandAsync(DropCommand command, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_carrierLoadingService.TryGetParcelId(command.CarrierId, out var mappedParcelId) ||
                mappedParcelId != command.ParcelId) {
                _logger.LogDebug(
                    "落格命令执行前校验失败：映射不存在或已变更 CarrierId={CarrierId} ParcelId={ParcelId}",
                    command.CarrierId,
                    command.ParcelId);
                return;
            }

            if (!_parcelManager.TryGet(command.ParcelId, out var parcel)) {
                _logger.LogWarning(
                    "落格命令执行前校验失败：包裹快照不存在 ParcelId={ParcelId} CarrierId={CarrierId}",
                    command.ParcelId,
                    command.CarrierId);
                return;
            }

            if (parcel.TargetChuteId != command.ChuteId && !command.IsForcedChutePass) {
                _logger.LogDebug(
                    "落格命令执行前校验失败：目标格口已变化 ParcelId={ParcelId} CarrierId={CarrierId} ExpectedChuteId={ExpectedChuteId} ActualChuteId={ActualChuteId}",
                    command.ParcelId,
                    command.CarrierId,
                    command.ChuteId,
                    parcel.TargetChuteId);
                return;
            }

            var isBoundToCarrier = false;
            foreach (var boundCarrierId in parcel.CarrierIds) {
                if (boundCarrierId == command.CarrierId) {
                    isBoundToCarrier = true;
                    break;
                }
            }

            if (!isBoundToCarrier) {
                _logger.LogDebug(
                    "落格命令执行前校验失败：包裹未绑定当前小车 ParcelId={ParcelId} CarrierId={CarrierId}",
                    command.ParcelId,
                    command.CarrierId);
                return;
            }

            if (!_carrierLoadingService.RemoveCarrierParcelMapping(command.CarrierId)) {
                _logger.LogDebug(
                    "落格命令执行前校验失败：映射已被其他路径移除 CarrierId={CarrierId} ParcelId={ParcelId}",
                    command.CarrierId,
                    command.ParcelId);
                return;
            }

            if (command.IsForcedChutePass) {
                await ExecuteForcedDropCommandAsync(command, parcel, cancellationToken).ConfigureAwait(false);
                return;
            }

            await ExecuteNormalDropCommandAsync(command, parcel).ConfigureAwait(false);
        }

        /// <summary>
        /// 执行普通落格命令。
        /// </summary>
        /// <param name="command">落格命令。</param>
        /// <param name="parcel">包裹快照。</param>
        /// <returns>异步任务。</returns>
        private async Task ExecuteNormalDropCommandAsync(DropCommand command, Core.Models.Parcel.ParcelInfo parcel) {
            if (!_chuteManager.TryGetChute(command.ChuteId, out var chute)) {
                _logger.LogWarning(
                    "落格异常 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ChuteId={ChuteId} 原因=未找到格口",
                    command.ParcelId,
                    parcel.BarCode,
                    command.CarrierId,
                    command.ChuteId);
                _carrierLoadingService.TryRestoreCarrierParcelMapping(command.CarrierId, command.ParcelId);
                return;
            }

            var safeChuteOpenCloseIntervalMs = ConfigurationValueHelper.GetPositiveOrDefault(
                CurrentTimingOptions.ChuteOpenCloseIntervalMs,
                SortingTaskTimingOptions.DefaultChuteOpenCloseIntervalMs);
            var dropped = await chute.DropAsync(
                parcel,
                command.ChangedAt,
                TimeSpan.FromMilliseconds(safeChuteOpenCloseIntervalMs)).ConfigureAwait(false);
            if (!dropped) {
                _carrierLoadingService.TryRestoreCarrierParcelMapping(command.CarrierId, command.ParcelId);
                _logger.LogWarning(
                    "落格异常 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ChuteId={ChuteId} 原因=落格调用返回失败",
                    command.ParcelId,
                    parcel.BarCode,
                    command.CarrierId,
                    command.ChuteId);
                return;
            }

            var marked = await _parcelManager.MarkDroppedAsync(command.ParcelId, command.ChuteId, command.ChangedAt, command.CurrentInductionCarrierId).ConfigureAwait(false);
            if (!marked) {
                _logger.LogWarning(
                    "落格异常 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ChuteId={ChuteId} 原因=落格后状态标记失败",
                    command.ParcelId,
                    parcel.BarCode,
                    command.CarrierId,
                    command.ChuteId);
            }

            var unbound = await _parcelManager.UnbindCarrierAsync(command.ParcelId, command.CarrierId, command.ChangedAt).ConfigureAwait(false);
            if (!unbound) {
                _logger.LogWarning(
                    "落格异常 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ChuteId={ChuteId} 原因=落格后解绑失败",
                    command.ParcelId,
                    parcel.BarCode,
                    command.CarrierId,
                    command.ChuteId);
            }

            if (!marked || !unbound) {
                _logger.LogWarning(
                    "落格异常 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ChuteId={ChuteId} 原因=落格后清理链路未完全成功",
                    command.ParcelId,
                    parcel.BarCode,
                    command.CarrierId,
                    command.ChuteId);
                _carrierLoadingService.ClearParcelTimeline(command.ParcelId);
                return;
            }

            var hasElapsedFromArrived = _carrierLoadingService.TryGetElapsedFromArrivedToDropped(command.ParcelId, command.ChangedAt, out var elapsedFromArrived, out var elapsedFromArrivedMs);
            var rawQueueCount = _carrierLoadingService.RawQueueCountSnapshot;
            var readyQueueCount = _carrierLoadingService.ReadyQueueCount;
            var inFlightCarrierParcelCount = _carrierLoadingService.InFlightCarrierParcelCount;
            var densityBucket = _carrierLoadingService.GetDensityBucketLabel(rawQueueCount, readyQueueCount, inFlightCarrierParcelCount);
            string loopTrackRealTimeSpeedMmpsStr;
            if (_logger.IsEnabled(LogLevel.Warning)
                && _carrierLoadingService.TryGetRealTimeSpeedMmps(out var realTimeSpeedMmps)) {
                loopTrackRealTimeSpeedMmpsStr = SortingValueFormatter.FormatSpeed(realTimeSpeedMmps);
            }
            else {
                loopTrackRealTimeSpeedMmpsStr = "N/A";
            }

            if (hasElapsedFromArrived) {
                _carrierLoadingService.RecordArrivedToDroppedElapsed(elapsedFromArrivedMs, densityBucket);
                var dropAlertThresholdMs = ConfigurationValueHelper.GetPositiveOrDefault(
                    CurrentTimingOptions.ParcelChainAlertThresholdMs,
                    SortingTaskTimingOptions.DefaultParcelChainAlertThresholdMs);
                if (elapsedFromArrivedMs > dropAlertThresholdMs) {
                    _carrierLoadingService.RecordArrivedToDroppedExceedance(densityBucket);
                    _logger.LogWarning(
                        "落格链路耗时超阈值告警 ChuteId={ChuteId} CarrierId={CarrierId} ParcelId={ParcelId} BarCode={BarCode} LoopTrackRealtimeSpeedMmps={LoopTrackRealtimeSpeedMmps} ElapsedMs={ElapsedMs} ThresholdMs={ThresholdMs} RawQueueCount={RawQueueCount} ReadyQueueCount={ReadyQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                        command.ChuteId,
                        command.CarrierId,
                        command.ParcelId,
                        parcel.BarCode,
                        loopTrackRealTimeSpeedMmpsStr,
                        elapsedFromArrivedMs,
                        dropAlertThresholdMs,
                        rawQueueCount,
                        readyQueueCount,
                        inFlightCarrierParcelCount,
                        densityBucket);
                }

                _logger.LogInformation(
                    "落格成功 ChuteId={ChuteId} CarrierId={CarrierId} ParcelId={ParcelId} BarCode={BarCode} LoopTrackRealtimeSpeedMmps={LoopTrackRealtimeSpeedMmps} [距离到达目标格口准备落格:{ElapsedFromArrived}] RawQueueCount={RawQueueCount} ReadyQueueCount={ReadyQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                    command.ChuteId,
                    command.CarrierId,
                    command.ParcelId,
                    parcel.BarCode,
                    loopTrackRealTimeSpeedMmpsStr,
                    elapsedFromArrived,
                    rawQueueCount,
                    readyQueueCount,
                    inFlightCarrierParcelCount,
                    densityBucket);
            }
            else {
                _logger.LogInformation(
                    "落格成功 ChuteId={ChuteId} CarrierId={CarrierId} ParcelId={ParcelId} BarCode={BarCode} LoopTrackRealtimeSpeedMmps={LoopTrackRealtimeSpeedMmps} RawQueueCount={RawQueueCount} ReadyQueueCount={ReadyQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                    command.ChuteId,
                    command.CarrierId,
                    command.ParcelId,
                    parcel.BarCode,
                    loopTrackRealTimeSpeedMmpsStr,
                    rawQueueCount,
                    readyQueueCount,
                    inFlightCarrierParcelCount,
                    densityBucket);
            }

            _carrierLoadingService.ClearParcelTimeline(command.ParcelId);
        }

        /// <summary>
        /// 执行强排经过命令。
        /// </summary>
        /// <param name="command">落格命令。</param>
        /// <param name="parcel">包裹快照。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task ExecuteForcedDropCommandAsync(
            DropCommand command,
            Core.Models.Parcel.ParcelInfo parcel,
            CancellationToken cancellationToken) {
            var barCode = parcel.BarCode;
            if (_carrierManager.TryGetCarrier(command.CarrierId, out var forcedCarrier) && forcedCarrier.IsLoaded) {
                var unloaded = await forcedCarrier.UnloadParcelAsync(cancellationToken).ConfigureAwait(false);
                if (!unloaded) {
                    _logger.LogWarning(
                        "强排卸货失败 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ForcedChuteId={ForcedChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId}",
                        command.ParcelId,
                        barCode,
                        command.CarrierId,
                        command.ChuteId,
                        command.CurrentInductionCarrierId);
                }
            }

            var marked = await _parcelManager.MarkDroppedAsync(command.ParcelId, command.ChuteId, command.ChangedAt, command.CurrentInductionCarrierId, cancellationToken).ConfigureAwait(false);
            var unbound = await _parcelManager.UnbindCarrierAsync(command.ParcelId, command.CarrierId, command.ChangedAt, cancellationToken).ConfigureAwait(false);
            if (!marked || !unbound) {
                _logger.LogWarning(
                    "强排后清理链路未完全成功 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ForcedChuteId={ForcedChuteId} Marked={Marked} Unbound={Unbound}",
                    command.ParcelId,
                    barCode,
                    command.CarrierId,
                    command.ChuteId,
                    marked,
                    unbound);
            }

            _carrierLoadingService.ClearParcelTimeline(command.ParcelId);
        }

        /// <summary>
        /// 释放落格命令去重占位。
        /// </summary>
        /// <param name="command">落格命令。</param>
        private void ReleaseDropCommandReservation(DropCommand command) {
            _dropCommandCarrierSet.TryRemove(command.CarrierId, out _);
            _dropCommandParcelSet.TryRemove(command.ParcelId, out _);
            _dropCommandCarrierChuteSet.TryRemove(new CarrierChuteCommandKey(command.CarrierId, command.ChuteId), out _);
        }

        /// <summary>
        /// 解析包裹条码；不存在时返回 null。
        /// </summary>
        /// <param name="parcelId">包裹 Id。</param>
        /// <returns>条码或 null。</returns>
        private string? ResolveParcelBarCode(long parcelId) {
            return _parcelManager.TryGet(parcelId, out var parcel) ? parcel.BarCode : null;
        }

        /// <summary>
        /// 计算两个小车在环形编号中的最小距离。
        /// </summary>
        /// <param name="sourceCarrierId">源小车编号。</param>
        /// <param name="targetCarrierId">目标小车编号。</param>
        /// <param name="carrierCount">小车总数。</param>
        /// <param name="carrierIndexMap">小车编号到索引映射。</param>
        /// <returns>最小环形距离；无法计算时返回 int.MaxValue。</returns>
        private static int GetCircularDistance(long sourceCarrierId, long targetCarrierId, int carrierCount, IReadOnlyDictionary<long, int> carrierIndexMap) {
            // 步骤1：根据小车编号查找源与目标索引，不可解析时返回最大值。
            if (!carrierIndexMap.TryGetValue(sourceCarrierId, out var sourceIndex)
                || !carrierIndexMap.TryGetValue(targetCarrierId, out var targetIndex)
                || carrierCount <= 0) {
                return int.MaxValue;
            }

            // 步骤2：计算正向索引差绝对值。
            var diff = Math.Abs(sourceIndex - targetIndex);
            // 步骤3：返回顺逆向距离中的最小值。
            return Math.Min(diff, carrierCount - diff);
        }

        /// <summary>
        /// 获取小车编号索引缓存；环形小车序列变化时自动重建。
        /// </summary>
        /// <param name="orderedCarrierIds">环形小车有序编号（由 GetOrderedCarrierIds 保证已排序）。</param>
        /// <returns>小车编号到索引映射。</returns>
        private IReadOnlyDictionary<long, int> GetOrBuildCarrierIndexMap(long[] orderedCarrierIds) {
            // 步骤1：计算当前小车序列签名。
            var signature = ComputeCarrierIndexSignature(orderedCarrierIds);
            lock (_carrierIndexCacheLock) {
                // 步骤2：在锁内校验缓存命中，命中则直接复用。
                if (_cachedCarrierIndexCount == orderedCarrierIds.Length && _cachedCarrierIndexSignature == signature) {
                    return _cachedCarrierIndexMap;
                }

                // 步骤3：缓存未命中，委托共享重建逻辑（安全兜底，正常流程下缓存应已由 GetOrderedCarrierIds 填充）。
                RebuildCarrierCacheLocked(orderedCarrierIds);
                return _cachedCarrierIndexMap;
            }
        }

        /// <summary>
        /// 在已持有 _carrierIndexCacheLock 的前提下，重建有序编号缓存与索引映射缓存。
        /// </summary>
        /// <param name="sortedIds">已按升序排列的小车编号数组。</param>
        private void RebuildCarrierCacheLocked(long[] sortedIds) {
            // 步骤1：按索引位置构建编号→索引映射。
            var indexMap = new Dictionary<long, int>(sortedIds.Length);
            for (var i = 0; i < sortedIds.Length; i++) {
                indexMap[sortedIds[i]] = i;
            }

            // 步骤2：原子更新所有缓存字段。
            _cachedOrderedCarrierIds = sortedIds;
            _cachedCarrierIndexMap = indexMap;
            _cachedCarrierIndexSignature = ComputeCarrierIndexSignature(sortedIds);
            _cachedCarrierIndexCount = sortedIds.Length;
        }

        /// <summary>
        /// 计算小车有序列表签名，用于索引缓存命中判断。
        /// </summary>
        /// <param name="orderedCarrierIds">环形小车有序编号。</param>
        /// <returns>签名值。</returns>
        private static int ComputeCarrierIndexSignature(long[] orderedCarrierIds) {
            // 步骤1：创建哈希累计器。
            var hash = new HashCode();
            // 步骤2：按顺序写入小车编号，确保顺序敏感。
            for (var index = 0; index < orderedCarrierIds.Length; index++) {
                hash.Add(orderedCarrierIds[index]);
            }

            // 步骤3：输出最终签名。
            return hash.ToHashCode();
        }

        /// <summary>
        /// 刷新分拣时序配置快照。
        /// </summary>
        /// <param name="options">最新分拣时序配置。</param>
        private void RefreshTimingOptionsSnapshot(SortingTaskTimingOptions options) {
            Volatile.Write(ref _timingOptionsSnapshot, options);
        }

        /// <summary>
        /// 释放配置热更新订阅资源。
        /// </summary>
        public void Dispose() {
            Volatile.Write(ref _dropCommandChannelCompleted, true);
            _dropCommandChannel.Writer.TryComplete();
            _dropCommandConsumerCts.Cancel();
            _dropCommandConsumerCts.Dispose();
            _timingOptionsChangedRegistration.Dispose();
        }
    }
}
