using System;
using System.Threading;
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
        private readonly ConcurrentDictionary<long, DateTime> _loadingTriggerBoundAtMap = new();
        private readonly ConcurrentDictionary<long, DateTime> _arrivedTargetChuteAtMap = new();
        private int _readyQueueCount;

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
        /// 待装车包裹队列数量。
        /// </summary>
        public int ReadyQueueCount => Volatile.Read(ref _readyQueueCount);

        /// <summary>
        /// 小车与包裹绑定映射。
        /// </summary>
        public IReadOnlyDictionary<long, long> CarrierParcelMap => _carrierParcelMap;

        /// <summary>
        /// 是否存在在途的小车-包裹绑定。
        /// </summary>
        public bool HasCarrierParcelMapping => !_carrierParcelMap.IsEmpty;

        /// <summary>
        /// 入队成熟包裹。
        /// </summary>
        public void EnqueueReadyParcel(ParcelInfo parcel) {
            _readyParcelQueue.Enqueue(parcel);
            Interlocked.Increment(ref _readyQueueCount);
        }

        /// <summary>
        /// 清空待装车包裹队列。
        /// </summary>
        public void ClearReadyQueue() {
            var removedCount = 0;
            while (_readyParcelQueue.TryDequeue(out _)) {
                removedCount++;
            }

            if (removedCount == 0) {
                return;
            }

            Interlocked.Add(ref _readyQueueCount, -removedCount);
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
        /// 记录包裹绑定上车触发时间。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="loadingTriggerOccurredAt">上车触发时间。</param>
        public void RecordLoadingTriggerBoundAt(long parcelId, DateTime loadingTriggerOccurredAt) {
            _loadingTriggerBoundAtMap[parcelId] = NormalizeLocalTime(loadingTriggerOccurredAt, "RecordLoadingTriggerBoundAt", parcelId);
        }

        /// <summary>
        /// 尝试获取包裹从创建到上车触发绑定的耗时文本。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="elapsedText">耗时文本。</param>
        /// <returns>是否成功。</returns>
        public bool TryGetElapsedFromCreatedToLoadingTrigger(long parcelId, out string elapsedText) {
            elapsedText = string.Empty;
            if (!_loadingTriggerBoundAtMap.TryGetValue(parcelId, out var loadingTriggerOccurredAt)) {
                return false;
            }

            if (!TryGetParcelCreatedAt(parcelId, out var parcelCreatedAt)) {
                return false;
            }

            elapsedText = FormatElapsed(parcelId, loadingTriggerOccurredAt - parcelCreatedAt);
            return true;
        }

        /// <summary>
        /// 记录包裹到达目标格口时间，并返回距离上一个链路节点的耗时文本。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="arrivedAt">到达时间。</param>
        /// <param name="previousNodeName">上一个链路节点名称。</param>
        /// <param name="elapsedText">耗时文本。</param>
        public void RecordArrivedTargetChute(
            long parcelId,
            DateTime arrivedAt,
            out string previousNodeName,
            out string elapsedText) {
            var localArrivedAt = NormalizeLocalTime(arrivedAt, "RecordArrivedTargetChute", parcelId);
            DateTime previousNodeAt;
            if (_loadingTriggerBoundAtMap.TryGetValue(parcelId, out var loadingTriggerOccurredAt)) {
                previousNodeName = "上车触发";
                previousNodeAt = loadingTriggerOccurredAt;
            }
            else if (TryGetParcelCreatedAt(parcelId, out var parcelCreatedAt)) {
                previousNodeName = "创建包裹";
                previousNodeAt = parcelCreatedAt;
            }
            else {
                previousNodeName = "创建包裹";
                previousNodeAt = localArrivedAt;
            }

            _arrivedTargetChuteAtMap[parcelId] = localArrivedAt;
            elapsedText = FormatElapsed(parcelId, localArrivedAt - previousNodeAt);
        }

        /// <summary>
        /// 尝试获取包裹从“到达目标格口准备落格”到“落格成功”的耗时文本。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="droppedAt">落格时间。</param>
        /// <param name="elapsedText">耗时文本。</param>
        /// <returns>是否成功。</returns>
        public bool TryGetElapsedFromArrivedToDropped(long parcelId, DateTime droppedAt, out string elapsedText) {
            elapsedText = string.Empty;
            if (!_arrivedTargetChuteAtMap.TryGetValue(parcelId, out var arrivedAt)) {
                return false;
            }

            var localDroppedAt = NormalizeLocalTime(droppedAt, "TryGetElapsedFromArrivedToDropped", parcelId);
            elapsedText = FormatElapsed(parcelId, localDroppedAt - arrivedAt);
            return true;
        }

        /// <summary>
        /// 清理包裹链路时间节点缓存。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        public void ClearParcelTimeline(long parcelId) {
            _loadingTriggerBoundAtMap.TryRemove(parcelId, out _);
            _arrivedTargetChuteAtMap.TryRemove(parcelId, out _);
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
                if (_readyParcelQueue.TryDequeue(out var parcel)) {
                    Interlocked.Decrement(ref _readyQueueCount);
                    if (!_carrierParcelMap.TryAdd(args.CarrierId, parcel.ParcelId)) {
                        EnqueueReadyParcel(parcel);
                        _logger.LogWarning(
                            "装车事件并发冲突，已存在映射回退包裹 CarrierId={CarrierId} ParcelId={ParcelId}",
                            args.CarrierId,
                            parcel.ParcelId);
                        return;
                    }

                    await _parcelManager.BindCarrierAsync(
                        parcel.ParcelId,
                        args.CarrierId,
                        args.ChangedAt).ConfigureAwait(false);

                    _logger.LogInformation(
                        "装车成功 CarrierId={CarrierId} ParcelId={ParcelId} RemainingReadyQueueCount={QueueCount}",
                        args.CarrierId,
                        parcel.ParcelId,
                        ReadyQueueCount);
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
                    args.ChangedAt).ConfigureAwait(false);

                _logger.LogInformation(
                    "卸货事件触发解绑 CarrierId={CarrierId} ParcelId={ParcelId}",
                    args.CarrierId,
                    oldParcelId);
            }
        }

        /// <summary>
        /// 在上车位根据当前感应位小车和上车位偏移尝试装车。
        /// </summary>
        public async ValueTask TryLoadParcelAtLoadingZoneAsync(
            long currentInductionCarrierId,
            DateTime changedAt,
            CancellationToken cancellationToken = default) {
            // 步骤 1：先确认当前存在待装车包裹，避免无效后续计算。
            cancellationToken.ThrowIfCancellationRequested();
            if (!_readyParcelQueue.TryPeek(out _)) {
                return;
            }

            // 步骤 2：校验小车总量与感应位编号范围，保障偏移计算输入有效。
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

            // 步骤 3：基于环形偏移计算上车位小车编号，并解析小车实例。
            var loadingCarrierId = CircularValueHelper.GetCounterClockwiseValue(
                currentCarrierValue,
                _carrierManager.LoadingZoneCarrierOffset,
                totalCarrierCount);

            if (!_carrierManager.TryGetCarrier(loadingCarrierId, out var loadingCarrier)) {
                _logger.LogWarning("未找到上车位小车，跳过装车 CarrierId={CarrierId}", loadingCarrierId);
                return;
            }

            // 步骤 4：上车位已装载时直接返回，避免重复装车。
            if (loadingCarrier.IsLoaded) {
                return;
            }

            // 步骤 5：从待装车队列消费包裹并触发装车动作。
            if (!_readyParcelQueue.TryDequeue(out var parcel)) {
                return;
            }
            Interlocked.Decrement(ref _readyQueueCount);

            var loaded = await loadingCarrier.LoadParcelAsync(parcel, []).ConfigureAwait(false);
            if (!loaded) {
                EnqueueReadyParcel(parcel);
                _logger.LogWarning(
                    "调用小车装车失败，包裹已回退到待装车队列 CarrierId={CarrierId} ParcelId={ParcelId}",
                    loadingCarrierId,
                    parcel.ParcelId);
                return;
            }

            // 步骤 6：装车成功后尝试建立映射；并发冲突时回退队列避免丢包。
            if (!_carrierParcelMap.TryAdd(loadingCarrierId, parcel.ParcelId)) {
                EnqueueReadyParcel(parcel);
                _logger.LogWarning(
                    "上车位装车后发现小车已存在包裹绑定，疑似并发装车竞争，当前包裹已回退到待装车队列 CarrierId={CarrierId} ParcelId={ParcelId}",
                    loadingCarrierId,
                    parcel.ParcelId);
                return;
            }

            await _parcelManager.BindCarrierAsync(parcel.ParcelId, loadingCarrierId, changedAt).ConfigureAwait(false);

            // 步骤 7：记录上车位装车结果。
            _logger.LogInformation(
                "上车位装车成功 CarrierId={CarrierId} ParcelId={ParcelId} RemainingReadyQueueCount={QueueCount}",
                loadingCarrierId,
                parcel.ParcelId,
                ReadyQueueCount);
        }

        /// <summary>
        /// 将时间归一化为本地时间语义。
        /// </summary>
        /// <param name="value">输入时间。</param>
        /// <returns>本地时间。</returns>
        private DateTime NormalizeLocalTime(DateTime value, string operation, long parcelId) {
            if (value == default) {
                _logger.LogWarning(
                    "链路时间节点值为默认值，已回退本地当前时间 Operation={Operation} ParcelId={ParcelId}",
                    operation,
                    parcelId);
                return DateTime.Now;
            }

            return value.Kind switch {
                DateTimeKind.Local => value,
                DateTimeKind.Utc => value.ToLocalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Local),
            };
        }

        /// <summary>
        /// 尝试从包裹编号恢复创建时间。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="createdAt">创建时间。</param>
        /// <returns>是否成功。</returns>
        private bool TryGetParcelCreatedAt(long parcelId, out DateTime createdAt) {
            createdAt = default;
            if (parcelId <= 0) {
                _logger.LogWarning("包裹编号无效，无法恢复创建时间 ParcelId={ParcelId}", parcelId);
                return false;
            }

            try {
                createdAt = new DateTime(parcelId, DateTimeKind.Local);
                return true;
            }
            catch (ArgumentOutOfRangeException) {
                _logger.LogWarning("包裹编号超出时间范围，无法恢复创建时间 ParcelId={ParcelId}", parcelId);
                return false;
            }
        }

        /// <summary>
        /// 格式化链路耗时文本。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="elapsed">耗时。</param>
        /// <returns>格式化字符串。</returns>
        private string FormatElapsed(long parcelId, TimeSpan elapsed) {
            if (elapsed < TimeSpan.Zero) {
                _logger.LogWarning(
                    "检测到链路耗时为负值，已按 00:00:00,000 输出 ParcelId={ParcelId} ElapsedMs={ElapsedMs}",
                    parcelId,
                    elapsed.TotalMilliseconds);
                return TimeSpan.Zero.ToString(@"hh\:mm\:ss\,fff");
            }

            return elapsed.ToString(@"hh\:mm\:ss\,fff");
        }
    }
}
