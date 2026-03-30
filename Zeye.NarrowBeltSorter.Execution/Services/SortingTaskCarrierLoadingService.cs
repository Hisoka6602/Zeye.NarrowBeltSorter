using System;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Models.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;
using Zeye.NarrowBeltSorter.Core.Utilities;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 分拣任务上车编排服务：负责成熟包裹队列消费、上车绑定与小车-包裹映射维护。
    /// </summary>
    public sealed class SortingTaskCarrierLoadingService {
        private readonly ILogger<SortingTaskCarrierLoadingService> _logger;
        private readonly ICarrierManager _carrierManager;
        private readonly IParcelManager _parcelManager;

        private readonly ConcurrentQueue<ParcelInfo> _readyParcelQueue = new();
        private readonly ConcurrentDictionary<long, long> _carrierParcelMap = new();

        /// <summary>
        /// 初始化上车编排服务。
        /// </summary>
        public SortingTaskCarrierLoadingService(
            ILogger<SortingTaskCarrierLoadingService> logger,
            ICarrierManager carrierManager,
            IParcelManager parcelManager) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _carrierManager = carrierManager ?? throw new ArgumentNullException(nameof(carrierManager));
            _parcelManager = parcelManager ?? throw new ArgumentNullException(nameof(parcelManager));
        }

        /// <summary>
        /// 透出当前感应位小车变化事件。
        /// </summary>
        public event EventHandler<Core.Events.Carrier.CurrentInductionCarrierChangedEventArgs>? CurrentInductionCarrierChanged {
            add => _carrierManager.CurrentInductionCarrierChanged += value;
            remove => _carrierManager.CurrentInductionCarrierChanged -= value;
        }

        /// <summary>
        /// 透出小车装载状态变化事件。
        /// </summary>
        public event EventHandler<Core.Events.Carrier.CarrierLoadStatusChangedEventArgs>? CarrierLoadStatusChanged {
            add => _carrierManager.CarrierLoadStatusChanged += value;
            remove => _carrierManager.CarrierLoadStatusChanged -= value;
        }

        /// <summary>
        /// 待装车包裹队列数量。
        /// </summary>
        public int ReadyQueueCount => _readyParcelQueue.Count;

        /// <summary>
        /// 小车与包裹绑定映射。
        /// </summary>
        public IReadOnlyDictionary<long, long> CarrierParcelMap => _carrierParcelMap;

        /// <summary>
        /// 入队成熟包裹。
        /// </summary>
        public void EnqueueReadyParcel(ParcelInfo parcel) {
            _readyParcelQueue.Enqueue(parcel);
        }

        /// <summary>
        /// 尝试获取小车绑定的包裹编号。
        /// </summary>
        /// <param name="carrierId">小车编号。</param>
        /// <param name="parcelId">包裹编号。</param>
        /// <returns>是否存在绑定。</returns>
        public bool TryGetParcelId(long carrierId, out long parcelId) {
            return _carrierParcelMap.TryGetValue(carrierId, out parcelId);
        }

        /// <summary>
        /// 移除小车绑定映射。
        /// </summary>
        /// <param name="carrierId">小车编号。</param>
        /// <returns>是否移除成功。</returns>
        public bool RemoveCarrierParcelMapping(long carrierId) {
            return _carrierParcelMap.TryRemove(carrierId, out _);
        }

        /// <summary>
        /// 处理小车装载状态变化（上车/卸货）。
        /// </summary>
        public async ValueTask HandleCarrierLoadStatusChangedAsync(
            Core.Events.Carrier.CarrierLoadStatusChangedEventArgs args,
            SystemState currentState,
            CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            if (currentState != SystemState.Running) {
                return;
            }

            if (args.NewIsLoaded) {
                if (_carrierParcelMap.ContainsKey(args.CarrierId)) {
                    return;
                }

                if (_readyParcelQueue.TryDequeue(out var parcel)) {
                    _carrierParcelMap[args.CarrierId] = parcel.ParcelId;
                    await _parcelManager.BindCarrierAsync(
                        parcel.ParcelId,
                        args.CarrierId,
                        DateTime.Now).ConfigureAwait(false);

                    _logger.LogInformation(
                        "装车成功 CarrierId={CarrierId} ParcelId={ParcelId} RemainingReadyQueueCount={QueueCount}",
                        args.CarrierId,
                        parcel.ParcelId,
                        _readyParcelQueue.Count);
                }
                else {
                    _logger.LogWarning("装车事件到达但待装车队列为空 CarrierId={CarrierId}", args.CarrierId);
                }

                return;
            }

            if (_carrierParcelMap.TryRemove(args.CarrierId, out var oldParcelId)) {
                await _parcelManager.UnbindCarrierAsync(
                    oldParcelId,
                    args.CarrierId,
                    DateTime.Now).ConfigureAwait(false);

                _logger.LogInformation(
                    "卸货事件触发解绑 CarrierId={CarrierId} ParcelId={ParcelId}",
                    args.CarrierId,
                    oldParcelId);
            }
        }

        /// <summary>
        /// 在上车位根据当前感应位小车和上车位偏移尝试装车。
        /// </summary>
        public async ValueTask TryLoadParcelAtLoadingZoneAsync(long currentInductionCarrierId, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_readyParcelQueue.TryPeek(out _)) {
                return;
            }

            var totalCarrierCount = _carrierManager.Carriers.Count;
            if (totalCarrierCount <= 0) {
                return;
            }

            if (currentInductionCarrierId is < int.MinValue or > int.MaxValue) {
                _logger.LogWarning(
                    "当前感应位小车编号超出 CircularValueHelper 计算范围 CarrierId={CarrierId}",
                    currentInductionCarrierId);
                return;
            }

            var currentCarrierValue = (int)currentInductionCarrierId;
            if (currentCarrierValue < 1 || currentCarrierValue > totalCarrierCount) {
                _logger.LogWarning(
                    "当前感应位小车编号不在环形编号范围内 CarrierId={CarrierId} TotalCarrierCount={TotalCarrierCount}",
                    currentInductionCarrierId,
                    totalCarrierCount);
                return;
            }

            var loadingCarrierId = CircularValueHelper.GetCounterClockwiseValue(
                currentCarrierValue,
                _carrierManager.LoadingZoneCarrierOffset,
                totalCarrierCount);

            if (!_carrierManager.TryGetCarrier(loadingCarrierId, out var loadingCarrier)) {
                _logger.LogWarning("未找到上车位小车，跳过装车 CarrierId={CarrierId}", loadingCarrierId);
                return;
            }

            if (loadingCarrier.IsLoaded) {
                return;
            }

            if (!_readyParcelQueue.TryDequeue(out var parcel)) {
                return;
            }

            var loaded = await loadingCarrier.LoadParcelAsync(parcel, []).ConfigureAwait(false);
            if (!loaded) {
                _readyParcelQueue.Enqueue(parcel);
                _logger.LogWarning(
                    "调用小车装车失败，包裹已回退到待装车队列 CarrierId={CarrierId} ParcelId={ParcelId}",
                    loadingCarrierId,
                    parcel.ParcelId);
                return;
            }

            _carrierParcelMap[loadingCarrierId] = parcel.ParcelId;
            await _parcelManager.BindCarrierAsync(parcel.ParcelId, loadingCarrierId, DateTime.Now).ConfigureAwait(false);

            _logger.LogInformation(
                "上车位装车成功 CarrierId={CarrierId} ParcelId={ParcelId} RemainingReadyQueueCount={QueueCount}",
                loadingCarrierId,
                parcel.ParcelId,
                _readyParcelQueue.Count);
        }
    }
}
