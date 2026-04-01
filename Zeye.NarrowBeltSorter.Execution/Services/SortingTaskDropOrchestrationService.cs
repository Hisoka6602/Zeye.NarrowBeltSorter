using System;
using System.Linq;
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

            var orderedCarrierIds = GetOrderedCarrierIds();
            if (orderedCarrierIds.Length == 0) {
                foreach (var mapping in _carrierLoadingService.CarrierParcelMap) {
                    _logger.LogWarning(
                        "落格跳过 ParcelId={ParcelId} CarrierId={CarrierId} 原因=环形小车未构建或小车列表为空",
                        mapping.Value,
                        mapping.Key);
                }

                return;
            }

            DetectApproachingTargetChute(args.NewCarrierId.Value, orderedCarrierIds);

            foreach (var pair in _carrierManager.ChuteCarrierOffsetMap) {
                var chuteId = pair.Key;
                var carrierIdAtChute = ResolveCarrierIdAtChute(args.NewCarrierId.Value, pair.Value, orderedCarrierIds);
                if (!carrierIdAtChute.HasValue) {
                    continue;
                }

                if (!_carrierLoadingService.TryGetParcelId(carrierIdAtChute.Value, out var parcelId)) {
                    continue;
                }

                if (!_parcelManager.TryGet(parcelId, out var parcel)) {
                    _logger.LogWarning(
                        "落格跳过 ParcelId={ParcelId} CarrierId={CarrierId} ChuteId={ChuteId} 原因=包裹快照不存在",
                        parcelId,
                        carrierIdAtChute.Value,
                        chuteId);
                    continue;
                }

                if (parcel.TargetChuteId != chuteId) {
                    _logger.LogDebug(
                        "落格跳过 ParcelId={ParcelId} CarrierId={CarrierId} CurrentChuteId={CurrentChuteId} TargetChuteId={TargetChuteId} 原因=未到目标格口",
                        parcelId,
                        carrierIdAtChute.Value,
                        chuteId,
                        parcel.TargetChuteId);
                    continue;
                }

                _logger.LogInformation(
                    "小车到达目标格口准备落格 ParcelId={ParcelId} CarrierId={CarrierId} TargetChuteId={ChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId}",
                    parcelId,
                    carrierIdAtChute.Value,
                    chuteId,
                    args.NewCarrierId.Value);

                if (!_chuteManager.TryGetChute(chuteId, out var chute)) {
                    _logger.LogWarning(
                        "落格异常 ParcelId={ParcelId} CarrierId={CarrierId} ChuteId={ChuteId} 原因=未找到格口",
                        parcelId,
                        carrierIdAtChute.Value,
                        chuteId);
                    continue;
                }

                var droppedAt = args.ChangedAt;
                var dropped = await chute.DropAsync(
                    parcel,
                    droppedAt,
                    TimeSpan.FromMilliseconds(safeChuteOpenCloseIntervalMs)).ConfigureAwait(false);
                if (!dropped) {
                    _logger.LogWarning(
                        "落格异常 ParcelId={ParcelId} CarrierId={CarrierId} ChuteId={ChuteId} 原因=落格调用返回失败",
                        parcelId,
                        carrierIdAtChute.Value,
                        chuteId);
                    continue;
                }

                var marked = await _parcelManager.MarkDroppedAsync(parcelId, chuteId, droppedAt).ConfigureAwait(false);
                if (!marked) {
                    _logger.LogWarning(
                        "落格异常 ParcelId={ParcelId} CarrierId={CarrierId} ChuteId={ChuteId} 原因=落格后状态标记失败",
                        parcelId,
                        carrierIdAtChute.Value,
                        chuteId);
                }

                var unbound = await _parcelManager.UnbindCarrierAsync(parcelId, carrierIdAtChute.Value, droppedAt).ConfigureAwait(false);
                if (!unbound) {
                    _logger.LogWarning(
                        "落格异常 ParcelId={ParcelId} CarrierId={CarrierId} ChuteId={ChuteId} 原因=落格后解绑失败",
                        parcelId,
                        carrierIdAtChute.Value,
                        chuteId);
                }

                var removedMapping = _carrierLoadingService.RemoveCarrierParcelMapping(carrierIdAtChute.Value);
                if (!removedMapping) {
                    _logger.LogWarning(
                        "落格异常 ParcelId={ParcelId} CarrierId={CarrierId} ChuteId={ChuteId} 原因=落格后内存映射移除失败",
                        parcelId,
                        carrierIdAtChute.Value,
                        chuteId);
                }

                if (!marked || !unbound || !removedMapping) {
                    _logger.LogWarning(
                        "落格异常 ParcelId={ParcelId} CarrierId={CarrierId} ChuteId={ChuteId} 原因=落格后清理链路未完全成功",
                        parcelId,
                        carrierIdAtChute.Value,
                        chuteId);
                    continue;
                }

                _logger.LogInformation(
                    "落格成功 ChuteId={ChuteId} CarrierId={CarrierId} ParcelId={ParcelId}",
                    chuteId,
                    carrierIdAtChute.Value,
                    parcelId);
            }

            DetectMissedChute(args.NewCarrierId.Value, orderedCarrierIds);
        }

        /// <summary>
        /// 获取按小车编号升序排列的小车编号数组。
        /// </summary>
        private long[] GetOrderedCarrierIds() {
            if (!_carrierManager.IsRingBuilt || _carrierManager.Carriers.Count == 0) {
                return [];
            }

            return _carrierManager.Carriers
                .Select(x => x.Id)
                .OrderBy(x => x)
                .ToArray();
        }

        /// <summary>
        /// 根据当前感应位小车和格口偏移量，解析位于目标格口前的小车编号。
        /// 偏移语义与上车位一致：按逆时针方向计算。
        /// </summary>
        private static long? ResolveCarrierIdAtChute(
            long currentInductionCarrierId,
            int chuteOffset,
            IReadOnlyList<long> orderedCarrierIds) {
            var currentIndex = -1;
            for (var i = 0; i < orderedCarrierIds.Count; i++) {
                if (orderedCarrierIds[i] == currentInductionCarrierId) {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex < 0) {
                return null;
            }

            var mappedIndex = WrapIndex(currentIndex - chuteOffset, orderedCarrierIds.Count);
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
        /// <returns>是否命中目标格口。</returns>
        private bool IsCarrierAtTargetChute(long carrierId, long targetChuteId, long currentInductionCarrierId, IReadOnlyList<long> orderedCarrierIds) {
            if (!_carrierManager.ChuteCarrierOffsetMap.TryGetValue(targetChuteId, out var offset)) {
                return false;
            }

            var mappedCarrierId = ResolveCarrierIdAtChute(currentInductionCarrierId, offset, orderedCarrierIds);
            return mappedCarrierId.HasValue && mappedCarrierId.Value == carrierId;
        }

        /// <summary>
        /// 检测并记录错过目标格口的小车包裹。
        /// </summary>
        /// <param name="currentInductionCarrierId">当前感应位小车编号。</param>
        /// <param name="orderedCarrierIds">环形小车有序编号。</param>
        private void DetectMissedChute(long currentInductionCarrierId, IReadOnlyList<long> orderedCarrierIds) {
            var staleCarrierIds = new List<long>();
            foreach (var stateEntry in _carrierAtTargetStates) {
                if (!_carrierLoadingService.TryGetParcelId(stateEntry.Key, out _)) {
                    staleCarrierIds.Add(stateEntry.Key);
                }
            }
            foreach (var staleCarrierId in staleCarrierIds) {
                _carrierAtTargetStates.TryRemove(staleCarrierId, out _);
            }

            foreach (var mapping in _carrierLoadingService.CarrierParcelMap) {
                var carrierId = mapping.Key;
                var parcelId = mapping.Value;
                if (!_parcelManager.TryGet(parcelId, out var parcel) || parcel.TargetChuteId <= 0) {
                    continue;
                }

                var isAtTarget = IsCarrierAtTargetChute(carrierId, parcel.TargetChuteId, currentInductionCarrierId, orderedCarrierIds);
                var wasAtTarget = _carrierAtTargetStates.TryGetValue(carrierId, out var previousAtTarget) && previousAtTarget;
                if (wasAtTarget && !isAtTarget) {
                    _logger.LogWarning(
                        "错过格口 ParcelId={ParcelId} CarrierId={CarrierId} TargetChuteId={TargetChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId}",
                        parcelId,
                        carrierId,
                        parcel.TargetChuteId,
                        currentInductionCarrierId);
                }

                _carrierAtTargetStates[carrierId] = isAtTarget;
            }
        }

        /// <summary>
        /// 检测并记录“靠近目标格口”状态：目标距离为 1 或 2 个小车。
        /// </summary>
        /// <param name="currentInductionCarrierId">当前感应位小车编号。</param>
        /// <param name="orderedCarrierIds">环形小车有序编号。</param>
        private void DetectApproachingTargetChute(long currentInductionCarrierId, IReadOnlyList<long> orderedCarrierIds) {
            // 步骤1：构建当前环形拓扑索引映射，用于后续距离计算。
            if (orderedCarrierIds.Count == 0) {
                return;
            }

            var carrierIndexMap = GetOrBuildCarrierIndexMap(orderedCarrierIds);

            // 步骤2：遍历已绑定包裹，定位每个目标格口对应的小车位置。
            foreach (var mapping in _carrierLoadingService.CarrierParcelMap) {
                var carrierId = mapping.Key;
                var parcelId = mapping.Value;
                if (!_parcelManager.TryGet(parcelId, out var parcel) || parcel.TargetChuteId <= 0) {
                    continue;
                }

                if (!_carrierManager.ChuteCarrierOffsetMap.TryGetValue(parcel.TargetChuteId, out var targetOffset)) {
                    _logger.LogWarning(
                        "靠近目标格口判定失败 ParcelId={ParcelId} CarrierId={CarrierId} TargetChuteId={TargetChuteId} 原因=目标格口偏移未配置",
                        parcelId,
                        carrierId,
                        parcel.TargetChuteId);
                    continue;
                }

                var targetCarrierIdAtChute = ResolveCarrierIdAtChute(currentInductionCarrierId, targetOffset, orderedCarrierIds);
                if (!targetCarrierIdAtChute.HasValue) {
                    _logger.LogWarning(
                        "靠近目标格口判定失败 ParcelId={ParcelId} CarrierId={CarrierId} TargetChuteId={TargetChuteId} 原因=无法解析目标格口对应小车",
                        parcelId,
                        carrierId,
                        parcel.TargetChuteId);
                    continue;
                }

                // 步骤3：计算环形距离并记录靠近窗口（1~2）日志。
                var distanceToTarget = GetCircularDistance(carrierId, targetCarrierIdAtChute.Value, orderedCarrierIds.Count, carrierIndexMap);
                if (distanceToTarget is 1 or 2) {
                    _logger.LogDebug(
                        "小车靠近目标格口 ParcelId={ParcelId} CarrierId={CarrierId} TargetChuteId={TargetChuteId} CurrentTargetCarrierId={TargetCarrierId} DistanceToTarget={DistanceToTarget}",
                        parcelId,
                        carrierId,
                        parcel.TargetChuteId,
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
        /// <param name="orderedCarrierIds">环形小车有序编号。</param>
        /// <returns>小车编号到索引映射。</returns>
        private IReadOnlyDictionary<long, int> GetOrBuildCarrierIndexMap(IReadOnlyList<long> orderedCarrierIds) {
            // 步骤1：计算当前小车序列签名。
            var signature = ComputeCarrierIndexSignature(orderedCarrierIds);
            lock (_carrierIndexCacheLock) {
                // 步骤2：在锁内校验缓存命中，命中则直接复用。
                if (_cachedCarrierIndexCount == orderedCarrierIds.Count && _cachedCarrierIndexSignature == signature) {
                    return _cachedCarrierIndexMap;
                }

                // 步骤3：缓存未命中，重建小车编号索引映射。
                var indexMap = new Dictionary<long, int>(orderedCarrierIds.Count);
                for (var index = 0; index < orderedCarrierIds.Count; index++) {
                    indexMap[orderedCarrierIds[index]] = index;
                }

                // 步骤4：更新缓存并返回。
                _cachedCarrierIndexMap = indexMap;
                _cachedCarrierIndexSignature = signature;
                _cachedCarrierIndexCount = orderedCarrierIds.Count;
                return _cachedCarrierIndexMap;
            }
        }

        /// <summary>
        /// 计算小车有序列表签名，用于索引缓存命中判断。
        /// </summary>
        /// <param name="orderedCarrierIds">环形小车有序编号。</param>
        /// <returns>签名值。</returns>
        private static int ComputeCarrierIndexSignature(IReadOnlyList<long> orderedCarrierIds) {
            // 步骤1：创建哈希累计器。
            var hash = new HashCode();
            // 步骤2：按顺序写入小车编号，确保顺序敏感。
            for (var index = 0; index < orderedCarrierIds.Count; index++) {
                hash.Add(orderedCarrierIds[index]);
            }

            // 步骤3：输出最终签名。
            return hash.ToHashCode();
        }
    }
}
