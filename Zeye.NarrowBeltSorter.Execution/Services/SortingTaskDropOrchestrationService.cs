using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 分拣任务落格编排服务：负责小车到位映射、命中目标格口后的落格执行与解绑。
    /// </summary>
    public sealed class SortingTaskDropOrchestrationService {

        /// <summary>
        /// 格口开门到关门的间隔时间。
        /// </summary>
        private static readonly TimeSpan ChuteOpenCloseInterval = TimeSpan.FromMilliseconds(300);

        private readonly ILogger<SortingTaskDropOrchestrationService> _logger;
        private readonly ICarrierManager _carrierManager;
        private readonly IParcelManager _parcelManager;
        private readonly IChuteManager _chuteManager;
        private readonly SortingTaskCarrierLoadingService _carrierLoadingService;
        private readonly ConcurrentDictionary<long, bool> _carrierAtTargetStates = [];

        /// <summary>
        /// 初始化落格编排服务。
        /// </summary>
        public SortingTaskDropOrchestrationService(
            ILogger<SortingTaskDropOrchestrationService> logger,
            ICarrierManager carrierManager,
            IParcelManager parcelManager,
            IChuteManager chuteManager,
            SortingTaskCarrierLoadingService carrierLoadingService) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _carrierManager = carrierManager ?? throw new ArgumentNullException(nameof(carrierManager));
            _parcelManager = parcelManager ?? throw new ArgumentNullException(nameof(parcelManager));
            _chuteManager = chuteManager ?? throw new ArgumentNullException(nameof(chuteManager));
            _carrierLoadingService = carrierLoadingService ?? throw new ArgumentNullException(nameof(carrierLoadingService));
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

            await _carrierLoadingService.TryLoadParcelAtLoadingZoneAsync(args.NewCarrierId.Value, cancellationToken).ConfigureAwait(false);

            var orderedCarrierIds = GetOrderedCarrierIds();
            if (orderedCarrierIds.Length == 0) {
                return;
            }

            foreach (var pair in _carrierManager.ChuteCarrierOffsetMap) {
                var chuteId = pair.Key;
                var carrierIdAtChute = ResolveCarrierIdAtChute(args.NewCarrierId.Value, pair.Value, orderedCarrierIds);
                if (!carrierIdAtChute.HasValue) {
                    continue;
                }

                if (!_carrierLoadingService.TryGetParcelId(carrierIdAtChute.Value, out var parcelId)) {
                    continue;
                }

                if (!_parcelManager.TryGet(parcelId, out var parcel) || parcel.TargetChuteId != chuteId) {
                    continue;
                }

                _logger.LogInformation(
                    "小车靠近目标格口 ParcelId={ParcelId} CarrierId={CarrierId} TargetChuteId={ChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId}",
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

                var droppedAt = DateTime.Now;
                var dropped = await chute.DropAsync(parcel, droppedAt, ChuteOpenCloseInterval).ConfigureAwait(false);
                if (!dropped) {
                    _logger.LogWarning(
                        "落格异常 ParcelId={ParcelId} CarrierId={CarrierId} ChuteId={ChuteId} 原因=落格调用返回失败",
                        parcelId,
                        carrierIdAtChute.Value,
                        chuteId);
                    continue;
                }

                await _parcelManager.MarkDroppedAsync(parcelId, chuteId, droppedAt).ConfigureAwait(false);
                await _parcelManager.UnbindCarrierAsync(parcelId, carrierIdAtChute.Value, DateTime.Now).ConfigureAwait(false);
                _carrierLoadingService.RemoveCarrierParcelMapping(carrierIdAtChute.Value);

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

            var mappedIndex = WrapIndex(currentIndex + chuteOffset, orderedCarrierIds.Count);
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
            var staleCarrierIds = _carrierAtTargetStates.Keys
                .Where(carrierId => !_carrierLoadingService.TryGetParcelId(carrierId, out _))
                .ToArray();
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
    }
}
