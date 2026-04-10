using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Models.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;
using Zeye.NarrowBeltSorter.Core.Options.Sorting;
using Zeye.NarrowBeltSorter.Core.Utilities;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 分拣任务上车编排服务：负责成熟包裹队列消费、上车绑定与小车-包裹映射维护。
    /// </summary>
    public sealed class SortingTaskCarrierLoadingService {
        private readonly ILogger<SortingTaskCarrierLoadingService> _logger;
        private readonly ICarrierManager _carrierManager;
        private readonly IParcelManager _parcelManager;
        private readonly IOptionsMonitor<SortingTaskTimingOptions> _sortingTaskTimingOptionsMonitor;

        private readonly ConcurrentQueue<ParcelInfo> _readyParcelQueue = new();
        private readonly ConcurrentDictionary<long, long> _carrierParcelMap = new();
        private readonly ConcurrentDictionary<long, DateTime> _loadingTriggerBoundAtMap = new();
        private readonly ConcurrentDictionary<long, DateTime> _loadedAtMap = new();
        private readonly ConcurrentDictionary<long, DateTime> _arrivedTargetChuteAtMap = new();
        private int _readyQueueCount;
        private int _rawQueueCountSnapshot;

        /// <summary>
        /// 阶段统计：创建包裹 → 上车触发。
        /// </summary>
        private readonly SortingChainLatencyStats _createdToLoadingTriggerStats = new();

        /// <summary>
        /// 阶段统计：上车触发/创建 → 上车成功。
        /// </summary>
        private readonly SortingChainLatencyStats _triggerToLoadedStats = new();

        /// <summary>
        /// 阶段统计：上车成功 → 到达目标格口。
        /// </summary>
        private readonly SortingChainLatencyStats _loadedToArrivedStats = new();

        /// <summary>
        /// 阶段统计：到达目标格口 → 落格成功。
        /// </summary>
        private readonly SortingChainLatencyStats _arrivedToDroppedStats = new();

        /// <summary>
        /// 完成链路数计数（每 50 次完整落格输出一次 P50/P95/P99 统计日志）。
        /// </summary>
        private long _completedChainCount;

        /// <summary>
        /// 初始化上车编排服务。
        /// </summary>
        public SortingTaskCarrierLoadingService(
            ILogger<SortingTaskCarrierLoadingService> logger,
            ICarrierManager carrierManager,
            IParcelManager parcelManager,
            IOptionsMonitor<SortingTaskTimingOptions> sortingTaskTimingOptionsMonitor) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _carrierManager = carrierManager ?? throw new ArgumentNullException(nameof(carrierManager));
            _parcelManager = parcelManager ?? throw new ArgumentNullException(nameof(parcelManager));
            _sortingTaskTimingOptionsMonitor = sortingTaskTimingOptionsMonitor ?? throw new ArgumentNullException(nameof(sortingTaskTimingOptionsMonitor));
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
        /// 在途小车-包裹绑定数量。
        /// </summary>
        public int InFlightCarrierParcelCount => _carrierParcelMap.Count;

        /// <summary>
        /// 原始包裹队列数量快照（由主编排服务更新）。
        /// </summary>
        public int RawQueueCountSnapshot => Volatile.Read(ref _rawQueueCountSnapshot);

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
            // 步骤：每成功出队一项同步递减计数器，保持计数与队列内容始终一致。
            // 不在循环结束后做 Exchange(0)，避免并发入队在循环末尾到归零之间产生"队列非空但计数为 0"，
            // 导致后续 TryDequeue+Decrement 将计数减为负值。
            // 若并发 EnqueueReadyParcel 在 Enqueue 后、Increment 前被本循环出队，
            // 计数可短暂为 -1，随后对应 Increment 立即将其修正回 0，不影响业务正确性。
            while (_readyParcelQueue.TryDequeue(out _)) {
                Interlocked.Decrement(ref _readyQueueCount);
            }
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
        /// 恢复小车绑定映射（落格失败时回滚令牌，确保后续触发可重试）。
        /// </summary>
        /// <param name="carrierId">小车编号。</param>
        /// <param name="parcelId">包裹编号。</param>
        /// <returns>是否恢复成功（若并发路径已重新建立映射则返回 false）。</returns>
        public bool TryRestoreCarrierParcelMapping(long carrierId, long parcelId) {
            return _carrierParcelMap.TryAdd(carrierId, parcelId);
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
        /// 记录包裹上车成功时间节点。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="loadedAt">上车成功时间。</param>
        public void RecordLoadedAt(long parcelId, DateTime loadedAt) {
            _loadedAtMap[parcelId] = NormalizeLocalTime(loadedAt, "RecordLoadedAt", parcelId);
        }

        /// <summary>
        /// 记录"创建→上车触发"链路阶段延迟样本到对应密度分桶统计。
        /// </summary>
        /// <param name="elapsedMs">耗时（毫秒）。</param>
        /// <param name="densityBucket">密度分桶标签（Low/Medium/High）。</param>
        public void RecordCreatedToLoadingTriggerElapsed(double elapsedMs, string densityBucket) {
            _createdToLoadingTriggerStats.Record(elapsedMs, densityBucket);
        }

        /// <summary>
        /// 记录"创建包裹→上车触发"阶段超阈值样本，用于误差率统计。
        /// </summary>
        /// <param name="densityBucket">密度分桶标签（Low/Medium/High）。</param>
        public void RecordCreatedToLoadingTriggerExceedance(string densityBucket) {
            _createdToLoadingTriggerStats.RecordExceedance(densityBucket);
        }

        /// <summary>
        /// 记录"上车成功→到达目标格口"链路阶段延迟样本到对应密度分桶统计。
        /// </summary>
        /// <param name="elapsedMs">耗时（毫秒）。</param>
        /// <param name="densityBucket">密度分桶标签（Low/Medium/High）。</param>
        public void RecordLoadedToArrivedElapsed(double elapsedMs, string densityBucket) {
            _loadedToArrivedStats.Record(elapsedMs, densityBucket);
        }

        /// <summary>
        /// 记录"上车成功→到达目标格口"阶段超阈值样本，用于误差率统计。
        /// </summary>
        /// <param name="densityBucket">密度分桶标签（Low/Medium/High）。</param>
        public void RecordLoadedToArrivedExceedance(string densityBucket) {
            _loadedToArrivedStats.RecordExceedance(densityBucket);
        }

        /// <summary>
        /// 记录"到达目标格口→落格成功"链路阶段延迟样本，并在每 50 次完整落格后输出 P50/P95/P99 统计日志。
        /// </summary>
        /// <param name="elapsedMs">耗时（毫秒）。</param>
        /// <param name="densityBucket">密度分桶标签（Low/Medium/High）。</param>
        public void RecordArrivedToDroppedElapsed(double elapsedMs, string densityBucket) {
            _arrivedToDroppedStats.Record(elapsedMs, densityBucket);
            var completedCount = Interlocked.Increment(ref _completedChainCount);
            if (completedCount % 50 == 0) {
                LogPeriodicLatencyStats();
            }
        }

        /// <summary>
        /// 记录"到达目标格口→落格成功"阶段超阈值样本，用于误差率统计。
        /// </summary>
        /// <param name="densityBucket">密度分桶标签（Low/Medium/High）。</param>
        public void RecordArrivedToDroppedExceedance(string densityBucket) {
            _arrivedToDroppedStats.RecordExceedance(densityBucket);
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
        /// 尝试获取包裹从创建到上车触发绑定的耗时毫秒数（用于统计，不经过字符串格式化与解析）。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="elapsedMs">耗时毫秒值。</param>
        /// <returns>是否成功。</returns>
        public bool TryGetCreatedToLoadingTriggerElapsedMs(long parcelId, out double elapsedMs) {
            elapsedMs = 0;
            if (!_loadingTriggerBoundAtMap.TryGetValue(parcelId, out var loadingTriggerOccurredAt)) {
                return false;
            }

            if (!TryGetParcelCreatedAt(parcelId, out var parcelCreatedAt)) {
                return false;
            }

            var elapsed = loadingTriggerOccurredAt - parcelCreatedAt;
            elapsedMs = Math.Max(0, elapsed.TotalMilliseconds);
            return true;
        }

        /// <summary>
        /// 尝试获取包裹从上车触发（或创建，若无触发记录）到上车成功的耗时文本与毫秒数。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="loadedAt">上车成功时间。</param>
        /// <param name="previousNodeName">上一链路节点名称。</param>
        /// <param name="elapsedText">耗时文本。</param>
        /// <param name="elapsedMs">耗时毫秒值（用于阈值判断与统计）。</param>
        /// <returns>是否成功。</returns>
        public bool TryGetElapsedFromTriggerToLoaded(
            long parcelId,
            DateTime loadedAt,
            out string previousNodeName,
            out string elapsedText,
            out double elapsedMs) {
            var localLoadedAt = NormalizeLocalTime(loadedAt, "TryGetElapsedFromTriggerToLoaded", parcelId);
            DateTime previousNodeAt;
            if (_loadingTriggerBoundAtMap.TryGetValue(parcelId, out var triggerAt)) {
                previousNodeName = "上车触发";
                previousNodeAt = triggerAt;
            }
            else if (TryGetParcelCreatedAt(parcelId, out var createdAt)) {
                previousNodeName = "创建包裹";
                previousNodeAt = createdAt;
            }
            else {
                previousNodeName = string.Empty;
                elapsedText = string.Empty;
                elapsedMs = 0;
                return false;
            }

            var elapsed = localLoadedAt - previousNodeAt;
            elapsedMs = Math.Max(0, elapsed.TotalMilliseconds);
            elapsedText = FormatElapsed(parcelId, elapsed);
            return true;
        }

        /// <summary>
        /// 记录包裹到达目标格口时间，并返回距离上一个链路节点的耗时文本与毫秒数。
        /// 优先以"上车成功"为上一节点，其次为"上车触发"，兜底为"创建包裹"。
        /// 当三者均无法获取时，<paramref name="hasValidPreviousNode"/> 返回 false，调用方应跳过阶段统计记录。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="arrivedAt">到达时间。</param>
        /// <param name="previousNodeName">上一个链路节点名称。</param>
        /// <param name="elapsedText">耗时文本。</param>
        /// <param name="elapsedMs">耗时毫秒值（用于阈值判断与统计）。</param>
        /// <param name="hasValidPreviousNode">是否成功找到有效的上一节点；false 时 elapsedMs 为 0，不应计入统计。</param>
        public void RecordArrivedTargetChute(
            long parcelId,
            DateTime arrivedAt,
            out string previousNodeName,
            out string elapsedText,
            out double elapsedMs,
            out bool hasValidPreviousNode) {
            var localArrivedAt = NormalizeLocalTime(arrivedAt, "RecordArrivedTargetChute", parcelId);
            DateTime previousNodeAt;
            if (_loadedAtMap.TryGetValue(parcelId, out var loadedAt)) {
                previousNodeName = "上车成功";
                previousNodeAt = loadedAt;
                hasValidPreviousNode = true;
            }
            else if (_loadingTriggerBoundAtMap.TryGetValue(parcelId, out var triggerAt)) {
                previousNodeName = "上车触发";
                previousNodeAt = triggerAt;
                hasValidPreviousNode = true;
            }
            else if (TryGetParcelCreatedAt(parcelId, out var parcelCreatedAt)) {
                previousNodeName = "创建包裹";
                previousNodeAt = parcelCreatedAt;
                hasValidPreviousNode = true;
            }
            else {
                // 步骤：无有效上一节点时，将 previousNodeAt 设为到达时间，使 elapsedMs=0，并标记无效，调用方应跳过统计。
                previousNodeName = "未知";
                previousNodeAt = localArrivedAt;
                hasValidPreviousNode = false;
            }

            _arrivedTargetChuteAtMap[parcelId] = localArrivedAt;
            var elapsed = localArrivedAt - previousNodeAt;
            elapsedMs = Math.Max(0, elapsed.TotalMilliseconds);
            elapsedText = FormatElapsed(parcelId, elapsed);
        }

        /// <summary>
        /// 尝试获取包裹从"到达目标格口准备落格"到"落格成功"的耗时文本与毫秒数。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="droppedAt">落格时间。</param>
        /// <param name="elapsedText">耗时文本。</param>
        /// <param name="elapsedMs">耗时毫秒值（用于阈值判断与统计）。</param>
        /// <returns>是否成功。</returns>
        public bool TryGetElapsedFromArrivedToDropped(long parcelId, DateTime droppedAt, out string elapsedText, out double elapsedMs) {
            elapsedText = string.Empty;
            elapsedMs = 0;
            if (!_arrivedTargetChuteAtMap.TryGetValue(parcelId, out var arrivedAt)) {
                return false;
            }

            var localDroppedAt = NormalizeLocalTime(droppedAt, "TryGetElapsedFromArrivedToDropped", parcelId);
            var elapsed = localDroppedAt - arrivedAt;
            elapsedMs = Math.Max(0, elapsed.TotalMilliseconds);
            elapsedText = FormatElapsed(parcelId, elapsed);
            return true;
        }

        /// <summary>
        /// 清理包裹链路时间节点缓存。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        public void ClearParcelTimeline(long parcelId) {
            _loadingTriggerBoundAtMap.TryRemove(parcelId, out _);
            _loadedAtMap.TryRemove(parcelId, out _);
            _arrivedTargetChuteAtMap.TryRemove(parcelId, out _);
        }

        /// <summary>
        /// 清理全部包裹链路时间节点缓存。
        /// </summary>
        public void ClearAllParcelTimelines() {
            _loadingTriggerBoundAtMap.Clear();
            _loadedAtMap.Clear();
            _arrivedTargetChuteAtMap.Clear();
        }

        /// <summary>
        /// 根据统一队列快照获取密度分桶标签。
        /// </summary>
        /// <param name="rawQueueCount">原始队列数量。</param>
        /// <param name="readyQueueCount">待装车队列数量。</param>
        /// <param name="inFlightCarrierParcelCount">在途小车-包裹映射数量。</param>
        /// <returns>密度分桶标签（Low/Medium/High）。</returns>
        public string GetDensityBucketLabel(int rawQueueCount, int readyQueueCount, int inFlightCarrierParcelCount) {
            var total = rawQueueCount + readyQueueCount + inFlightCarrierParcelCount;
            // 步骤：阈值按“低负载≤10、中负载≤30、高负载>30”分层，便于现场按同一口径对比密度区间。
            return total switch {
                <= 10 => "Low",
                <= 30 => "Medium",
                _ => "High"
            };
        }

        /// <summary>
        /// 更新原始队列数量快照。
        /// </summary>
        /// <param name="rawQueueCount">原始队列数量。</param>
        public void UpdateRawQueueCountSnapshot(int rawQueueCount) {
            Volatile.Write(ref _rawQueueCountSnapshot, Math.Max(0, rawQueueCount));
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
                // 步骤：若小车已由上车位装载路径抢先建立映射，则跳过事件驱动装载路径，
                // 防止双重出队导致后续包裹 FIFO 顺序被破坏。
                if (_carrierParcelMap.ContainsKey(args.CarrierId)) {
                    _logger.LogDebug(
                        "装车事件跳过：小车已在映射中，由上车位路径处理 CarrierId={CarrierId}",
                        args.CarrierId);
                    return;
                }

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

                    // 步骤：记录上车成功时间节点，支撑上车触发→上车成功阶段耗时观测。
                    RecordLoadedAt(parcel.ParcelId, args.ChangedAt);
                    var rawQueueCount = RawQueueCountSnapshot;
                    var readyQueueCount = ReadyQueueCount;
                    var inFlightCount = InFlightCarrierParcelCount;
                    var densityBucket = GetDensityBucketLabel(rawQueueCount, readyQueueCount, inFlightCount);
                    if (TryGetElapsedFromTriggerToLoaded(parcel.ParcelId, args.ChangedAt, out var prevNodeName, out var elapsedFromTrigger, out var elapsedFromTriggerMs)) {
                        _triggerToLoadedStats.Record(elapsedFromTriggerMs, densityBucket);
                        _logger.LogInformation(
                            "装车成功 CarrierId={CarrierId} ParcelId={ParcelId} [距离{PreviousNodeName}:{ElapsedFromTrigger}] RemainingReadyQueueCount={QueueCount} RawQueueCount={RawQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                            args.CarrierId,
                            parcel.ParcelId,
                            prevNodeName,
                            elapsedFromTrigger,
                            readyQueueCount,
                            rawQueueCount,
                            inFlightCount,
                            densityBucket);
                        var alertThresholdMs = ConfigurationValueHelper.GetPositiveOrDefault(
                            _sortingTaskTimingOptionsMonitor.CurrentValue.ParcelChainAlertThresholdMs,
                            SortingTaskTimingOptions.DefaultParcelChainAlertThresholdMs);
                        if (elapsedFromTriggerMs > alertThresholdMs) {
                            _triggerToLoadedStats.RecordExceedance(densityBucket);
                            _logger.LogWarning(
                                "装车链路耗时超阈值告警 CarrierId={CarrierId} ParcelId={ParcelId} PreviousNodeName={PreviousNodeName} ElapsedMs={ElapsedMs} ThresholdMs={ThresholdMs} RawQueueCount={RawQueueCount} ReadyQueueCount={ReadyQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                                args.CarrierId,
                                parcel.ParcelId,
                                prevNodeName,
                                elapsedFromTriggerMs,
                                alertThresholdMs,
                                rawQueueCount,
                                readyQueueCount,
                                inFlightCount,
                                densityBucket);
                        }
                    }
                    else {
                        _logger.LogInformation(
                            "装车成功 CarrierId={CarrierId} ParcelId={ParcelId} RemainingReadyQueueCount={QueueCount}",
                            args.CarrierId,
                            parcel.ParcelId,
                            readyQueueCount);
                    }
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

                // 步骤：解绑后同步清理包裹链路时间节点缓存，防止长期运行中的内存累积。
                ClearParcelTimeline(oldParcelId);
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

            // 步骤 5：从待装车队列消费包裹，先原子占位映射再触发装车。
            // 背景：LoadParcelAsync 触发 LoadStatusChanged 事件，
            // 事件经 PublishEventAsync 通过 ThreadPool 回调 HandleCarrierLoadStatusChangedAsync，
            // 若 TryAdd 在 LoadParcelAsync 之后调用，事件回调可能抢先出队另一包裹并占位，
            // 导致本路径 TryAdd 失败、包裹回退队尾，破坏 FIFO 顺序。
            // 先占位后触发，使事件回调的 ContainsKey 守卫可提前拦截，彻底消除此竞态。
            if (!_readyParcelQueue.TryDequeue(out var parcel)) {
                return;
            }
            Interlocked.Decrement(ref _readyQueueCount);

            // 步骤 6：先原子占位映射槽，占位失败说明小车已被并发绑定，回退包裹并退出。
            if (!_carrierParcelMap.TryAdd(loadingCarrierId, parcel.ParcelId)) {
                EnqueueReadyParcel(parcel);
                _logger.LogWarning(
                    "上车位装车前发现小车已存在包裹绑定，疑似并发装车竞争，当前包裹已回退到待装车队列 CarrierId={CarrierId} ParcelId={ParcelId}",
                    loadingCarrierId,
                    parcel.ParcelId);
                return;
            }

            // 步骤 7：映射占位成功后执行装车；失败时原子释放占位并回退包裹，防止映射与实际载货状态不一致。
            var loaded = await loadingCarrier.LoadParcelAsync(parcel, []).ConfigureAwait(false);
            if (!loaded) {
                _carrierParcelMap.TryRemove(loadingCarrierId, out _);
                EnqueueReadyParcel(parcel);
                _logger.LogWarning(
                    "调用小车装车失败，已回退映射占位与待装车队列 CarrierId={CarrierId} ParcelId={ParcelId}",
                    loadingCarrierId,
                    parcel.ParcelId);
                return;
            }

            await _parcelManager.BindCarrierAsync(parcel.ParcelId, loadingCarrierId, changedAt).ConfigureAwait(false);

            // 步骤 8：记录上车成功时间节点并输出含链路耗时与阈值告警的装车日志。
            RecordLoadedAt(parcel.ParcelId, changedAt);
            var loadedRawQueueCount = RawQueueCountSnapshot;
            var loadedReadyQueueCount = ReadyQueueCount;
            var loadedInFlightCount = InFlightCarrierParcelCount;
            var loadedDensityBucket = GetDensityBucketLabel(loadedRawQueueCount, loadedReadyQueueCount, loadedInFlightCount);
            if (TryGetElapsedFromTriggerToLoaded(parcel.ParcelId, changedAt, out var loadedPrevNodeName, out var loadedElapsedFromTrigger, out var loadedElapsedFromTriggerMs)) {
                _triggerToLoadedStats.Record(loadedElapsedFromTriggerMs, loadedDensityBucket);
                _logger.LogInformation(
                    "上车位装车成功 CarrierId={CarrierId} ParcelId={ParcelId} CurrentInductionCarrierId={CurrentInductionCarrierId} LoadingZoneOffset={LoadingZoneOffset} [距离{PreviousNodeName}:{ElapsedFromTrigger}] RemainingReadyQueueCount={QueueCount} RawQueueCount={RawQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                    loadingCarrierId,
                    parcel.ParcelId,
                    currentInductionCarrierId,
                    _carrierManager.LoadingZoneCarrierOffset,
                    loadedPrevNodeName,
                    loadedElapsedFromTrigger,
                    loadedReadyQueueCount,
                    loadedRawQueueCount,
                    loadedInFlightCount,
                    loadedDensityBucket);
                // IOptionsMonitor.CurrentValue 为内存属性，不触发 I/O，符合热路径约束。
                var alertThresholdMs = ConfigurationValueHelper.GetPositiveOrDefault(
                    _sortingTaskTimingOptionsMonitor.CurrentValue.ParcelChainAlertThresholdMs,
                    SortingTaskTimingOptions.DefaultParcelChainAlertThresholdMs);
                if (loadedElapsedFromTriggerMs > alertThresholdMs) {
                    _triggerToLoadedStats.RecordExceedance(loadedDensityBucket);
                    _logger.LogWarning(
                        "上车位装车链路耗时超阈值告警 CarrierId={CarrierId} ParcelId={ParcelId} CurrentInductionCarrierId={CurrentInductionCarrierId} LoadingZoneOffset={LoadingZoneOffset} PreviousNodeName={PreviousNodeName} ElapsedMs={ElapsedMs} ThresholdMs={ThresholdMs} RawQueueCount={RawQueueCount} ReadyQueueCount={ReadyQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                        loadingCarrierId,
                        parcel.ParcelId,
                        currentInductionCarrierId,
                        _carrierManager.LoadingZoneCarrierOffset,
                        loadedPrevNodeName,
                        loadedElapsedFromTriggerMs,
                        alertThresholdMs,
                        loadedRawQueueCount,
                        loadedReadyQueueCount,
                        loadedInFlightCount,
                        loadedDensityBucket);
                }
            }
            else {
                _logger.LogInformation(
                    "上车位装车成功 CarrierId={CarrierId} ParcelId={ParcelId} CurrentInductionCarrierId={CurrentInductionCarrierId} LoadingZoneOffset={LoadingZoneOffset} RemainingReadyQueueCount={QueueCount}",
                    loadingCarrierId,
                    parcel.ParcelId,
                    currentInductionCarrierId,
                    _carrierManager.LoadingZoneCarrierOffset,
                    ReadyQueueCount);
            }
        }

        /// <summary>
        /// 密度分桶标签数组（静态复用，避免热路径重复分配）。
        /// </summary>
        private static readonly string[] DensityBuckets = ["Low", "Medium", "High"];

        /// <summary>
        /// 输出所有链路阶段 P50/P95/P99 百分位统计日志（按密度分桶分别输出）。
        /// </summary>
        private void LogPeriodicLatencyStats() {
            // 步骤：依次输出四个链路阶段在低/中/高密度分桶下的延迟百分位，便于分析各密度区间下的抖动情况。
            foreach (var bucket in DensityBuckets) {
                LogBucketStats("创建→上车触发", _createdToLoadingTriggerStats, bucket);
                LogBucketStats("触发→上车成功", _triggerToLoadedStats, bucket);
                LogBucketStats("上车成功→到达格口", _loadedToArrivedStats, bucket);
                LogBucketStats("到达格口→落格成功", _arrivedToDroppedStats, bucket);
            }
        }

        /// <summary>
        /// 输出单个阶段在指定密度分桶下的 P50/P95/P99 及误差率日志。
        /// </summary>
        /// <param name="stageName">阶段名称。</param>
        /// <param name="stats">对应阶段的统计实例。</param>
        /// <param name="densityBucket">密度分桶标签（Low/Medium/High）。</param>
        private void LogBucketStats(string stageName, SortingChainLatencyStats stats, string densityBucket) {
            if (!stats.TryGetStats(densityBucket, out var p50, out var p95, out var p99, out var count)) {
                return;
            }

            // 步骤：同时输出误差率（超阈值次数/总记录次数），帮助按密度区间量化触发精度。
            stats.TryGetExceedanceRate(densityBucket, out var errorRate, out var exceedanceCount, out var totalCount);
            _logger.LogInformation(
                "链路延迟统计 Stage={StageName} DensityBucket={DensityBucket} P50={P50:F1}ms P95={P95:F1}ms P99={P99:F1}ms SampleCount={SampleCount} ExceedanceCount={ExceedanceCount} TotalCount={TotalCount} ErrorRate={ErrorRate:P1}",
                stageName,
                densityBucket,
                p50,
                p95,
                p99,
                count,
                exceedanceCount,
                totalCount,
                errorRate);
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
                DateTimeKind.Utc => WarnAndSpecifyLocalKind(value, operation, parcelId),
                _ => WarnAndSpecifyLocalKind(value, operation, parcelId),
            };
        }

        /// <summary>
        /// 记录告警并按本地时间语义重新标记时间类型。
        /// </summary>
        /// <param name="value">输入时间。</param>
        /// <param name="operation">操作名称。</param>
        /// <param name="parcelId">包裹编号。</param>
        /// <returns>本地时间语义值。</returns>
        private DateTime WarnAndSpecifyLocalKind(DateTime value, string operation, long parcelId) {
            _logger.LogWarning(
                "链路时间节点 Kind 非 Local，已按本地时间语义重置 Kind Operation={Operation} ParcelId={ParcelId} OriginalKind={OriginalKind}",
                operation,
                parcelId,
                value.Kind);
            return DateTime.SpecifyKind(value, DateTimeKind.Local);
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
                return TimeSpan.Zero.ToString(@"dd\.hh\:mm\:ss\,fff");
            }

            return elapsed.ToString(@"dd\.hh\:mm\:ss\,fff");
        }
    }
}
