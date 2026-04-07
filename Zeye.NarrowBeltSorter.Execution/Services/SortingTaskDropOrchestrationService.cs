using System;
using System.Threading.Tasks;
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
    public sealed class SortingTaskDropOrchestrationService {

        private readonly ILogger<SortingTaskDropOrchestrationService> _logger;
        private readonly ICarrierManager _carrierManager;
        private readonly IParcelManager _parcelManager;
        private readonly IChuteManager _chuteManager;
        private readonly SortingTaskCarrierLoadingService _carrierLoadingService;
        private readonly IOptionsMonitor<SortingTaskTimingOptions> _sortingTaskTimingOptionsMonitor;

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
        }

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

            var safeChuteOpenCloseIntervalMs = ConfigurationValueHelper.GetPositiveOrDefault(
                _sortingTaskTimingOptionsMonitor.CurrentValue.ChuteOpenCloseIntervalMs,
                SortingTaskTimingOptions.DefaultChuteOpenCloseIntervalMs);

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
                    _logger.LogWarning(
                        "落格跳过 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} 原因=环形小车未构建或小车列表为空",
                        mapping.Value,
                        ParcelBarCodeLogHelper.GetNormalizedBarCode(_parcelManager, mapping.Value),
                        mapping.Key);
                }

                return;
            }

            // 步骤2：预先构建索引映射，供热路径各扫描分支共享，避免重复构建与 O(n) 线性扫描。
            var carrierIndexMap = GetOrBuildCarrierIndexMap(orderedCarrierIds);
            // 步骤3：靠近格口检测仅输出 Debug 日志；生产环境未开启 Debug 时跳过整段 O(k) 扫描，避免高频热路径空转。
            if (_logger.IsEnabled(LogLevel.Debug)) {
                await DetectApproachingTargetChuteAsync(args.NewCarrierId.Value, args.ChangedAt, orderedCarrierIds, carrierIndexMap, cancellationToken).ConfigureAwait(false);
            }

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
                    _logger.LogWarning(
                        "落格跳过 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ChuteId={ChuteId} 原因=包裹快照不存在",
                        parcelId,
                        ParcelBarCodeLogHelper.GetNormalizedBarCode(_parcelManager, parcelId),
                        carrierIdAtChute.Value,
                        chuteId);
                    continue;
                }

                var barCode = ParcelBarCodeLogHelper.Normalize(parcel.BarCode);
                if (!_chuteManager.TryGetChute(chuteId, out var chute)) {
                    _logger.LogWarning(
                        "落格异常 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ChuteId={ChuteId} 原因=未找到格口",
                        parcelId,
                        barCode,
                        carrierIdAtChute.Value,
                        chuteId);
                    continue;
                }

                // 步骤4：经过强排格口即视为已物理卸货，直接执行落格与清载清链路，不依赖目标格口匹配。
                if (chute.IsForced && parcel.TargetChuteId != chuteId) {
                    var forcedDroppedAt = args.ChangedAt;
                    var forcedDropped = await chute.DropAsync(
                        parcel,
                        forcedDroppedAt,
                        TimeSpan.FromMilliseconds(safeChuteOpenCloseIntervalMs)).ConfigureAwait(false);
                    if (!forcedDropped) {
                        _logger.LogWarning(
                            "强排格口落格异常 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ChuteId={ChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId} 原因=落格调用返回失败",
                            parcelId,
                            barCode,
                            carrierIdAtChute.Value,
                            chuteId,
                            args.NewCarrierId.Value);
                        _carrierLoadingService.ClearParcelTimeline(parcelId);
                        continue;
                    }

                    var forcedCleanupCompleted = await TryFinalizeDropAsync(
                        carrierIdAtChute.Value,
                        parcelId,
                        chuteId,
                        barCode,
                        args.NewCarrierId.Value,
                        forcedDroppedAt,
                        isForcedDrop: true,
                        cancellationToken).ConfigureAwait(false);
                    _carrierLoadingService.ClearParcelTimeline(parcelId);
                    if (!forcedCleanupCompleted) {
                        continue;
                    }

                    _logger.LogInformation(
                        "经过强排格口已强制卸货 ChuteId={ChuteId} CarrierId={CarrierId} ParcelId={ParcelId} BarCode={BarCode} CurrentInductionCarrierId={CurrentInductionCarrierId}",
                        chuteId,
                        carrierIdAtChute.Value,
                        parcelId,
                        barCode,
                        args.NewCarrierId.Value);
                    continue;
                }

                if (parcel.TargetChuteId != chuteId) {
                    _logger.LogDebug(
                        "落格跳过 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} CurrentChuteId={CurrentChuteId} TargetChuteId={TargetChuteId} 原因=未到目标格口",
                        parcelId,
                        barCode,
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
                        barCode,
                        carrierIdAtChute.Value,
                        chuteId);
                }

                _logger.LogInformation(
                    "小车到达目标格口准备落格 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} TargetChuteId={ChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId} [距离 {PreviousNodeName}: {ElapsedFromPrevious}] RawQueueCount={RawQueueCount} ReadyQueueCount={ReadyQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                    parcelId,
                    barCode,
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
                    _sortingTaskTimingOptionsMonitor.CurrentValue.ParcelChainAlertThresholdMs,
                    SortingTaskTimingOptions.DefaultParcelChainAlertThresholdMs);
                if (hasValidPreviousNode && elapsedFromPreviousMs > arrivalAlertThresholdMs) {
                    _carrierLoadingService.RecordLoadedToArrivedExceedance(densityBucket);
                    _logger.LogWarning(
                        "到达目标格口链路耗时超阈值告警 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} TargetChuteId={ChuteId} ChuteOffset={ChuteOffset} CurrentInductionCarrierId={CurrentInductionCarrierId} PreviousNodeName={PreviousNodeName} ElapsedMs={ElapsedMs} ThresholdMs={ThresholdMs} RawQueueCount={RawQueueCount} ReadyQueueCount={ReadyQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                        parcelId,
                        barCode,
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

                var droppedAt = args.ChangedAt;
                var dropped = await chute.DropAsync(
                    parcel,
                    droppedAt,
                    TimeSpan.FromMilliseconds(safeChuteOpenCloseIntervalMs)).ConfigureAwait(false);
                if (!dropped) {
                    _logger.LogWarning(
                        "落格异常 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ChuteId={ChuteId} 原因=落格调用返回失败",
                        parcelId,
                        barCode,
                        carrierIdAtChute.Value,
                        chuteId);
                    continue;
                }

                var cleanupCompleted = await TryFinalizeDropAsync(
                    carrierIdAtChute.Value,
                    parcelId,
                    chuteId,
                    barCode,
                    args.NewCarrierId.Value,
                    droppedAt,
                    isForcedDrop: false,
                    cancellationToken).ConfigureAwait(false);
                if (!cleanupCompleted) {
                    _carrierLoadingService.ClearParcelTimeline(parcelId);
                    continue;
                }

                var hasElapsedFromArrived = _carrierLoadingService.TryGetElapsedFromArrivedToDropped(parcelId, droppedAt, out var elapsedFromArrived, out var elapsedFromArrivedMs);
                rawQueueCount = _carrierLoadingService.RawQueueCountSnapshot;
                readyQueueCount = _carrierLoadingService.ReadyQueueCount;
                inFlightCarrierParcelCount = _carrierLoadingService.InFlightCarrierParcelCount;
                densityBucket = _carrierLoadingService.GetDensityBucketLabel(rawQueueCount, readyQueueCount, inFlightCarrierParcelCount);
                if (hasElapsedFromArrived) {
                    _carrierLoadingService.RecordArrivedToDroppedElapsed(elapsedFromArrivedMs, densityBucket);
                    // 步骤：落格阶段耗时超阈值时记录误差率并输出告警，便于量化落格执行端延迟。
                    var dropAlertThresholdMs = ConfigurationValueHelper.GetPositiveOrDefault(
                        _sortingTaskTimingOptionsMonitor.CurrentValue.ParcelChainAlertThresholdMs,
                        SortingTaskTimingOptions.DefaultParcelChainAlertThresholdMs);
                    if (elapsedFromArrivedMs > dropAlertThresholdMs) {
                        _carrierLoadingService.RecordArrivedToDroppedExceedance(densityBucket);
                        _logger.LogWarning(
                            "落格链路耗时超阈值告警 ChuteId={ChuteId} CarrierId={CarrierId} ParcelId={ParcelId} BarCode={BarCode} CurrentInductionCarrierId={CurrentInductionCarrierId} ElapsedMs={ElapsedMs} ThresholdMs={ThresholdMs} RawQueueCount={RawQueueCount} ReadyQueueCount={ReadyQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                            chuteId,
                            carrierIdAtChute.Value,
                            parcelId,
                            barCode,
                            args.NewCarrierId.Value,
                            elapsedFromArrivedMs,
                            dropAlertThresholdMs,
                            rawQueueCount,
                            readyQueueCount,
                            inFlightCarrierParcelCount,
                            densityBucket);
                    }

                    _logger.LogInformation(
                        "落格成功 ChuteId={ChuteId} CarrierId={CarrierId} ParcelId={ParcelId} BarCode={BarCode} CurrentInductionCarrierId={CurrentInductionCarrierId} [距离到达目标格口准备落格:{ElapsedFromArrived}] RawQueueCount={RawQueueCount} ReadyQueueCount={ReadyQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                        chuteId,
                        carrierIdAtChute.Value,
                        parcelId,
                        barCode,
                        args.NewCarrierId.Value,
                        elapsedFromArrived,
                        rawQueueCount,
                        readyQueueCount,
                        inFlightCarrierParcelCount,
                        densityBucket);
                }
                else {
                    _logger.LogInformation(
                        "落格成功 ChuteId={ChuteId} CarrierId={CarrierId} ParcelId={ParcelId} BarCode={BarCode} CurrentInductionCarrierId={CurrentInductionCarrierId} RawQueueCount={RawQueueCount} ReadyQueueCount={ReadyQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                        chuteId,
                        carrierIdAtChute.Value,
                        parcelId,
                        barCode,
                        args.NewCarrierId.Value,
                        rawQueueCount,
                        readyQueueCount,
                        inFlightCarrierParcelCount,
                        densityBucket);
                }
                _carrierLoadingService.ClearParcelTimeline(parcelId);
            }

            DetectMissedChute(args.NewCarrierId.Value, orderedCarrierIds, carrierIndexMap);
        }

        /// <summary>
        /// 落格后强制执行小车卸货，确保载货状态与包裹引用与物理一致。
        /// </summary>
        /// <param name="carrierId">小车编号。</param>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="chuteId">格口编号。</param>
        /// <param name="barCode">条码。</param>
        /// <param name="currentInductionCarrierId">当前感应区小车编号。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>卸货是否成功。</returns>
        private async ValueTask<bool> TryUnloadCarrierAfterDropAsync(
            long carrierId,
            long parcelId,
            long chuteId,
            string? barCode,
            long currentInductionCarrierId,
            CancellationToken cancellationToken = default) {
            if (!_carrierManager.TryGetCarrier(carrierId, out var carrier)) {
                _logger.LogWarning(
                    "落格后小车卸货失败 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ChuteId={ChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId} 原因=未找到小车实例",
                    parcelId,
                    barCode,
                    carrierId,
                    chuteId,
                    currentInductionCarrierId);
                return false;
            }

            // 步骤1：已处于非载货且包裹引用为空时直接视为成功，避免重复调用。
            if (!carrier.IsLoaded && carrier.Parcel is null) {
                return true;
            }

            // 步骤2：调用统一卸货能力，将 IsLoaded 置为 false 并清空 ParcelInfo。
            return await carrier.UnloadParcelAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 统一执行落格后的状态收敛：标记落格、解绑、移除映射并强制卸货。
        /// </summary>
        /// <param name="carrierId">小车编号。</param>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="chuteId">格口编号。</param>
        /// <param name="barCode">条码。</param>
        /// <param name="currentInductionCarrierId">当前感应区小车编号。</param>
        /// <param name="droppedAt">落格时间。</param>
        /// <param name="isForcedDrop">是否强排格口落格。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>收敛是否完整成功。</returns>
        private async ValueTask<bool> TryFinalizeDropAsync(
            long carrierId,
            long parcelId,
            long chuteId,
            string? barCode,
            long currentInductionCarrierId,
            DateTime droppedAt,
            bool isForcedDrop,
            CancellationToken cancellationToken = default) {
            var logPrefix = isForcedDrop ? "强排格口落格异常" : "落格异常";
            var completed = true;

            // 步骤1：先标记包裹已落格，确保业务状态与物理动作一致。
            var marked = await _parcelManager.MarkDroppedAsync(parcelId, chuteId, droppedAt, currentInductionCarrierId).ConfigureAwait(false);
            if (!marked) {
                _logger.LogWarning(
                    "{LogPrefix} ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ChuteId={ChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId} 原因=落格后状态标记失败",
                    logPrefix,
                    parcelId,
                    barCode,
                    carrierId,
                    chuteId,
                    currentInductionCarrierId);
                completed = false;
            }

            // 步骤2：解绑包裹与小车关系，避免后续链路误判仍在载货。
            var unbound = await _parcelManager.UnbindCarrierAsync(parcelId, carrierId, droppedAt).ConfigureAwait(false);
            if (!unbound) {
                _logger.LogWarning(
                    "{LogPrefix} ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ChuteId={ChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId} 原因=落格后解绑失败",
                    logPrefix,
                    parcelId,
                    barCode,
                    carrierId,
                    chuteId,
                    currentInductionCarrierId);
                completed = false;
            }

            // 步骤3：清理在途映射，防止同一包裹被重复处理。
            var removedMapping = _carrierLoadingService.RemoveCarrierParcelMapping(carrierId);
            if (!removedMapping) {
                _logger.LogWarning(
                    "{LogPrefix} ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ChuteId={ChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId} 原因=落格后内存映射移除失败",
                    logPrefix,
                    parcelId,
                    barCode,
                    carrierId,
                    chuteId,
                    currentInductionCarrierId);
                completed = false;
            }

            // 步骤4：强制卸货，将 IsLoaded 与 ParcelInfo 与物理状态对齐。
            var unloaded = await TryUnloadCarrierAfterDropAsync(
                carrierId,
                parcelId,
                chuteId,
                barCode,
                currentInductionCarrierId,
                cancellationToken).ConfigureAwait(false);
            if (!unloaded) {
                _logger.LogWarning(
                    "{LogPrefix} ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ChuteId={ChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId} 原因=落格后小车卸货失败",
                    logPrefix,
                    parcelId,
                    barCode,
                    carrierId,
                    chuteId,
                    currentInductionCarrierId);
                completed = false;
            }

            if (!completed) {
                _logger.LogWarning(
                    "{LogPrefix} ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} ChuteId={ChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId} 原因=落格后清理链路未完全成功",
                    logPrefix,
                    parcelId,
                    barCode,
                    carrierId,
                    chuteId,
                    currentInductionCarrierId);
            }

            return completed;
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
            var mappedIndex = WrapIndex(currentIndex - chuteOffset, orderedCarrierIds.Length);
            return orderedCarrierIds[mappedIndex];
        }

        /// <summary>
        /// 将任意索引映射到指定长度的环形区间内。
        /// </summary>
        private static int WrapIndex(int index, int length) {
            if (length <= 0) {
                return 0;
            }

            var result = index % length;
            return result < 0 ? result + length : result;
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
                        ParcelBarCodeLogHelper.Normalize(parcel.BarCode),
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
        /// <param name="orderedCarrierIds">环形小车有序编号。</param>
        /// <param name="carrierIndexMap">小车编号到索引的映射（由调用方预先构建，避免重复构建）。</param>
        private async ValueTask DetectApproachingTargetChuteAsync(
            long currentInductionCarrierId,
            DateTime changedAt,
            long[] orderedCarrierIds,
            IReadOnlyDictionary<long, int> carrierIndexMap,
            CancellationToken cancellationToken = default) {
            // 步骤1：遍历已绑定包裹，定位每个目标格口对应的小车位置。
            if (orderedCarrierIds.Length == 0) {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            foreach (var mapping in _carrierLoadingService.CarrierParcelMap) {
                var carrierId = mapping.Key;
                var parcelId = mapping.Value;
                if (!_parcelManager.TryGet(parcelId, out var parcel) || parcel.TargetChuteId <= 0) {
                    continue;
                }
                var barCode = ParcelBarCodeLogHelper.Normalize(parcel.BarCode);

                if (!_carrierManager.ChuteCarrierOffsetMap.TryGetValue(parcel.TargetChuteId, out var targetOffset)) {
                    _logger.LogWarning(
                        "靠近目标格口判定失败 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} TargetChuteId={TargetChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId} 原因=目标格口偏移未配置",
                        parcelId,
                        barCode,
                        carrierId,
                        parcel.TargetChuteId,
                        currentInductionCarrierId);
                    continue;
                }

                var targetCarrierIdAtChute = ResolveCarrierIdAtChute(currentInductionCarrierId, targetOffset, orderedCarrierIds, carrierIndexMap);
                if (!targetCarrierIdAtChute.HasValue) {
                    _logger.LogWarning(
                        "靠近目标格口判定失败 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} TargetChuteId={TargetChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId} 原因=无法解析目标格口对应小车",
                        parcelId,
                        barCode,
                        carrierId,
                        parcel.TargetChuteId,
                        currentInductionCarrierId);
                    continue;
                }

                // 步骤2：计算环形距离并记录靠近窗口（距离目标格口 2 个小车）日志。
                var distanceToTarget = GetCircularDistance(carrierId, targetCarrierIdAtChute.Value, orderedCarrierIds.Length, carrierIndexMap);
                if (distanceToTarget == 2) {
                    var published = await _carrierManager.PublishLoadedCarrierEnteredChuteInductionAsync(new Core.Events.Carrier.LoadedCarrierEnteredChuteInductionEventArgs {
                        CarrierId = carrierId,
                        ChuteId = parcel.TargetChuteId,
                        ParcelId = parcelId,
                        CurrentInductionCarrierId = currentInductionCarrierId,
                        EnteredAt = changedAt,
                    }, cancellationToken).ConfigureAwait(false);
                    if (!published) {
                        _logger.LogWarning(
                            "小车靠近目标格口事件发布失败 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} TargetChuteId={TargetChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId} DistanceToTarget={DistanceToTarget}",
                            parcelId,
                            barCode,
                            carrierId,
                            parcel.TargetChuteId,
                            currentInductionCarrierId,
                            distanceToTarget);
                        continue;
                    }

                    _logger.LogDebug(
                        "小车靠近目标格口 ParcelId={ParcelId} BarCode={BarCode} CarrierId={CarrierId} TargetChuteId={TargetChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId} CurrentTargetCarrierId={TargetCarrierId} DistanceToTarget={DistanceToTarget}",
                        parcelId,
                        barCode,
                        carrierId,
                        parcel.TargetChuteId,
                        currentInductionCarrierId,
                        targetCarrierIdAtChute.Value,
                        distanceToTarget);
                }
            }
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
    }
}
