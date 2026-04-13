using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Enums.Sorting;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Models.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Sorting;
using Zeye.NarrowBeltSorter.Core.Options.Sorting;
using Zeye.NarrowBeltSorter.Core.Utilities;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 分拣任务上车编排服务：负责成熟包裹队列消费、上车绑定与小车-包裹映射维护。
    /// </summary>
    public sealed class SortingTaskCarrierLoadingService : IDisposable {
        /// <summary>
        /// 延迟占比平滑窗口最大允许大小。
        /// </summary>
        private const int MaxSmoothingWindowSize = 20;

        /// <summary>
        /// double 整数判定精度阈值（用于 FormatDouble 中判断是否为整数值）。
        /// </summary>
        private const double DoubleEpsilon = 1e-9;
        private readonly ILogger<SortingTaskCarrierLoadingService> _logger;
        private readonly ICarrierManager _carrierManager;
        private readonly IParcelManager _parcelManager;
        private readonly ILoadingMatchRealtimeSpeedProvider _speedProvider;
        private readonly IOptionsMonitor<SortingTaskTimingOptions> _sortingTaskTimingOptionsMonitor;
        private readonly IDisposable _timingOptionsChangedRegistration;
        private SortingTaskTimingOptions _timingOptionsSnapshot;

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
        /// 上车匹配补偿当前滞回激活状态（true 表示处于补偿态）。
        /// 由 Enter/Exit 滞回门限联合控制状态翻转，避免在阈值附近频繁切换。
        /// </summary>
        private bool _compensationHysteresisActive;

        /// <summary>
        /// 延迟占比平滑窗口缓冲（环形数组，大小与 LoadingMatchSmoothingWindowSize 同步；Lock 保护）。
        /// </summary>
        private double[] _delayRatioWindow = new double[1];

        /// <summary>
        /// 平滑窗口已填充元素数量（仅在锁内更新，与 _delayRatioWindowIndex 配合使用）。
        /// </summary>
        private int _delayRatioWindowFilled;

        /// <summary>
        /// 平滑窗口写入指针（环形推进）。
        /// </summary>
        private int _delayRatioWindowIndex;

        /// <summary>
        /// 平滑窗口访问锁（保护环形缓冲区读写与填充计数一致性）。
        /// </summary>
        private readonly object _smoothingWindowLock = new();

        /// <summary>
        /// 传感器事件通道自上次上车操作以来是否发生过丢弃（1=有，0=无）。
        /// 由 <see cref="SortingTaskOrchestrationService"/> 在检测到满载丢弃时通过 <see cref="NotifySensorEventDrop"/> 设置。
        /// </summary>
        private int _sensorEventDropSinceLastLoad;

        /// <summary>
        /// 初始化上车编排服务。
        /// </summary>
        public SortingTaskCarrierLoadingService(
            ILogger<SortingTaskCarrierLoadingService> logger,
            ICarrierManager carrierManager,
            IParcelManager parcelManager,
            ILoadingMatchRealtimeSpeedProvider speedProvider,
            IOptionsMonitor<SortingTaskTimingOptions> sortingTaskTimingOptionsMonitor) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _carrierManager = carrierManager ?? throw new ArgumentNullException(nameof(carrierManager));
            _parcelManager = parcelManager ?? throw new ArgumentNullException(nameof(parcelManager));
            _speedProvider = speedProvider ?? throw new ArgumentNullException(nameof(speedProvider));
            _sortingTaskTimingOptionsMonitor = sortingTaskTimingOptionsMonitor ?? throw new ArgumentNullException(nameof(sortingTaskTimingOptionsMonitor));
            _timingOptionsSnapshot = _sortingTaskTimingOptionsMonitor.CurrentValue ?? throw new InvalidOperationException("SortingTaskTimingOptions 不能为空。");
            _timingOptionsChangedRegistration = _sortingTaskTimingOptionsMonitor.OnChange(RefreshTimingOptionsSnapshot) ?? throw new InvalidOperationException("SortingTaskTimingOptions.OnChange 订阅失败。");
        }

        /// <summary>
        /// 当前分拣时序配置快照。
        /// </summary>
        private SortingTaskTimingOptions CurrentTimingOptions => Volatile.Read(ref _timingOptionsSnapshot);

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
        /// 尝试获取环线实时速度快照（单位：mm/s）。
        /// </summary>
        /// <param name="realTimeSpeedMmps">实时速度快照。</param>
        /// <returns>获取成功返回 true，否则返回 false。</returns>
        public bool TryGetRealTimeSpeedMmps(out decimal realTimeSpeedMmps) {
            return _speedProvider.TryGetSpeedMmps(out realTimeSpeedMmps);
        }

        /// <summary>
        /// 通知传感器事件通道发生了满载丢弃。
        /// 由 <see cref="SortingTaskOrchestrationService"/> 在检测到丢弃时调用，
        /// 设置标志位后在下一次上车操作的补偿门禁中触发 SensorChannelDropWriteExceeded 降级。
        /// </summary>
        public void NotifySensorEventDrop() {
            Interlocked.Exchange(ref _sensorEventDropSinceLastLoad, 1);
        }

        /// <summary>
        /// 入队成熟包裹。
        /// </summary>
        /// <param name="parcel">待入队的成熟包裹。</param>
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
                            CurrentTimingOptions.ParcelChainAlertThresholdMs,
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
            // 步骤1：先确认当前存在待装车包裹，避免无效后续计算。
            cancellationToken.ThrowIfCancellationRequested();
            if (!_readyParcelQueue.TryPeek(out var peekedParcel)) {
                return;
            }

            // 步骤2：校验小车总量与感应位编号范围，保障偏移计算输入有效。
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

            // 步骤3：基于环形偏移计算基准上车位小车编号。
            var baseLoadingCarrierId = CircularValueHelper.GetCounterClockwiseValue(
                currentCarrierValue,
                _carrierManager.LoadingZoneCarrierOffset,
                totalCarrierCount);

            // 步骤4：计算时序补偿，得到最终目标上车位小车编号；同时输出阶段一观测字段。
            var timingOptions = CurrentTimingOptions;
            var finalLoadingCarrierId = ResolveCompensatedLoadingCarrierId(
                baseLoadingCarrierId,
                peekedParcel.ParcelId,
                changedAt,
                totalCarrierCount,
                timingOptions,
                out var compensationState,
                out var fallbackReason,
                out var effectiveDelayMs,
                out var carrierPeriodMs,
                out var delayRatio,
                out var compensationSpeedMmps);

            if (!_carrierManager.TryGetCarrier(finalLoadingCarrierId, out var loadingCarrier)) {
                _logger.LogWarning("未找到上车位小车，跳过装车 CarrierId={CarrierId}", finalLoadingCarrierId);
                return;
            }

            // 步骤5：上车位已装载时直接返回，避免重复装车。
            if (loadingCarrier.IsLoaded) {
                return;
            }

            // 步骤6：从待装车队列消费包裹，先原子占位映射再触发装车。
            // 背景：LoadParcelAsync 触发 LoadStatusChanged 事件，
            // 事件经 PublishEventAsync 通过 ThreadPool 回调 HandleCarrierLoadStatusChangedAsync，
            // 若 TryAdd 在 LoadParcelAsync 之后调用，事件回调可能抢先出队另一包裹并占位，
            // 导致本路径 TryAdd 失败、包裹回退队尾，破坏 FIFO 顺序。
            // 先占位后触发，使事件回调的 ContainsKey 守卫可提前拦截，彻底消除此竞态。
            if (!_readyParcelQueue.TryDequeue(out var parcel)) {
                return;
            }
            Interlocked.Decrement(ref _readyQueueCount);

            // 步骤7：先原子占位映射槽，占位失败说明小车已被并发绑定，回退包裹并退出。
            if (!_carrierParcelMap.TryAdd(finalLoadingCarrierId, parcel.ParcelId)) {
                EnqueueReadyParcel(parcel);
                _logger.LogWarning(
                    "上车位装车前发现小车已存在包裹绑定，疑似并发装车竞争，当前包裹已回退到待装车队列 CarrierId={CarrierId} ParcelId={ParcelId}",
                    finalLoadingCarrierId,
                    parcel.ParcelId);
                return;
            }

            // 步骤8：映射占位成功后执行装车；失败时原子释放占位并回退包裹，防止映射与实际载货状态不一致。
            var loaded = await loadingCarrier.LoadParcelAsync(parcel, []).ConfigureAwait(false);
            if (!loaded) {
                _carrierParcelMap.TryRemove(finalLoadingCarrierId, out _);
                EnqueueReadyParcel(parcel);
                _logger.LogWarning(
                    "调用小车装车失败，已回退映射占位与待装车队列 CarrierId={CarrierId} ParcelId={ParcelId}",
                    finalLoadingCarrierId,
                    parcel.ParcelId);
                return;
            }

            await _parcelManager.BindCarrierAsync(parcel.ParcelId, finalLoadingCarrierId, changedAt).ConfigureAwait(false);

            // 步骤9：记录上车成功时间节点并输出含链路耗时、阈值告警与补偿可观测性字段的装车日志。
            RecordLoadedAt(parcel.ParcelId, changedAt);
            var loadedRawQueueCount = RawQueueCountSnapshot;
            var loadedReadyQueueCount = ReadyQueueCount;
            var loadedInFlightCount = InFlightCarrierParcelCount;
            var loadedDensityBucket = GetDensityBucketLabel(loadedRawQueueCount, loadedReadyQueueCount, loadedInFlightCount);

            if (TryGetElapsedFromTriggerToLoaded(parcel.ParcelId, changedAt, out var loadedPrevNodeName, out var loadedElapsedFromTrigger, out var loadedElapsedFromTriggerMs)) {
                _triggerToLoadedStats.Record(loadedElapsedFromTriggerMs, loadedDensityBucket);
                _logger.LogInformation(
                    "上车位装车成功 CarrierId={CarrierId} ParcelId={ParcelId} CurrentInductionCarrierId={CurrentInductionCarrierId} BaseLoadingCarrierId={BaseLoadingCarrierId} LoadingZoneOffset={LoadingZoneOffset} Delta={Delta} CompensationState={CompensationState} FallbackReason={FallbackReason} LoopTrackRealtimeSpeedMmps={LoopTrackRealtimeSpeedMmps} CarrierPitchMm={CarrierPitchMm} CarrierPeriodMs={CarrierPeriodMs} EffectiveDelayMs={EffectiveDelayMs} DelayRatio={DelayRatio} [距离{PreviousNodeName}:{ElapsedFromTrigger}] RemainingReadyQueueCount={QueueCount} RawQueueCount={RawQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                    finalLoadingCarrierId,
                    parcel.ParcelId,
                    currentInductionCarrierId,
                    baseLoadingCarrierId,
                    _carrierManager.LoadingZoneCarrierOffset,
                    timingOptions.LoadingMatchCompensationDelta,
                    compensationState,
                    fallbackReason,
                    FormatSpeedValue(compensationSpeedMmps),
                    timingOptions.CarrierPitchMm,
                    FormatDouble(carrierPeriodMs),
                    FormatDouble(effectiveDelayMs),
                    FormatDouble(delayRatio),
                    loadedPrevNodeName,
                    loadedElapsedFromTrigger,
                    loadedReadyQueueCount,
                    loadedRawQueueCount,
                    loadedInFlightCount,
                    loadedDensityBucket);

                var alertThresholdMs = ConfigurationValueHelper.GetPositiveOrDefault(
                    timingOptions.ParcelChainAlertThresholdMs,
                    SortingTaskTimingOptions.DefaultParcelChainAlertThresholdMs);
                if (loadedElapsedFromTriggerMs > alertThresholdMs) {
                    _triggerToLoadedStats.RecordExceedance(loadedDensityBucket);
                    _logger.LogWarning(
                        "上车位装车链路耗时超阈值告警 CarrierId={CarrierId} ParcelId={ParcelId} CurrentInductionCarrierId={CurrentInductionCarrierId} BaseLoadingCarrierId={BaseLoadingCarrierId} LoadingZoneOffset={LoadingZoneOffset} Delta={Delta} CompensationState={CompensationState} FallbackReason={FallbackReason} LoopTrackRealtimeSpeedMmps={LoopTrackRealtimeSpeedMmps} CarrierPitchMm={CarrierPitchMm} CarrierPeriodMs={CarrierPeriodMs} EffectiveDelayMs={EffectiveDelayMs} DelayRatio={DelayRatio} PreviousNodeName={PreviousNodeName} ElapsedMs={ElapsedMs} ThresholdMs={ThresholdMs} RawQueueCount={RawQueueCount} ReadyQueueCount={ReadyQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                        finalLoadingCarrierId,
                        parcel.ParcelId,
                        currentInductionCarrierId,
                        baseLoadingCarrierId,
                        _carrierManager.LoadingZoneCarrierOffset,
                        timingOptions.LoadingMatchCompensationDelta,
                        compensationState,
                        fallbackReason,
                        FormatSpeedValue(compensationSpeedMmps),
                        timingOptions.CarrierPitchMm,
                        FormatDouble(carrierPeriodMs),
                        FormatDouble(effectiveDelayMs),
                        FormatDouble(delayRatio),
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
                    "上车位装车成功 CarrierId={CarrierId} ParcelId={ParcelId} CurrentInductionCarrierId={CurrentInductionCarrierId} BaseLoadingCarrierId={BaseLoadingCarrierId} LoadingZoneOffset={LoadingZoneOffset} Delta={Delta} CompensationState={CompensationState} FallbackReason={FallbackReason} LoopTrackRealtimeSpeedMmps={LoopTrackRealtimeSpeedMmps} CarrierPitchMm={CarrierPitchMm} CarrierPeriodMs={CarrierPeriodMs} EffectiveDelayMs={EffectiveDelayMs} DelayRatio={DelayRatio} RemainingReadyQueueCount={QueueCount}",
                    finalLoadingCarrierId,
                    parcel.ParcelId,
                    currentInductionCarrierId,
                    baseLoadingCarrierId,
                    _carrierManager.LoadingZoneCarrierOffset,
                    timingOptions.LoadingMatchCompensationDelta,
                    compensationState,
                    fallbackReason,
                    FormatSpeedValue(compensationSpeedMmps),
                    timingOptions.CarrierPitchMm,
                    FormatDouble(carrierPeriodMs),
                    FormatDouble(effectiveDelayMs),
                    FormatDouble(delayRatio),
                    ReadyQueueCount);
            }
        }

        /// <summary>
        /// 解析经时序补偿后的最终上车位小车编号，并输出补偿计算的可观测性字段。
        /// </summary>
        /// <param name="baseLoadingCarrierId">基于固定偏移计算的基准上车位小车编号。</param>
        /// <param name="parcelId">待上车包裹编号（用于查找上车触发时间记录）。</param>
        /// <param name="changedAt">当前感应位变化时间（上车匹配计算基准时间）。</param>
        /// <param name="totalCarrierCount">小车总数。</param>
        /// <param name="timingOptions">当前时序配置快照。</param>
        /// <param name="compensationState">输出：补偿状态（Active/Fallback）。</param>
        /// <param name="fallbackReason">输出：降级原因（Active 时为空字符串）。</param>
        /// <param name="effectiveDelayMs">输出：有效延迟（ms），门禁未通过时为 0。</param>
        /// <param name="carrierPeriodMs">输出：步距时间周期（ms），门禁未通过时为 0。</param>
        /// <param name="delayRatio">输出：延迟占比（百分比），门禁未通过时为 0。</param>
        /// <param name="realtimeSpeedMmps">输出：本次补偿计算使用的实时速度；不可用时为 null。</param>
        /// <returns>最终上车位小车编号。</returns>
        private int ResolveCompensatedLoadingCarrierId(
            int baseLoadingCarrierId,
            long parcelId,
            DateTime changedAt,
            int totalCarrierCount,
            SortingTaskTimingOptions timingOptions,
            out LoadingMatchCompensationState compensationState,
            out string fallbackReason,
            out double effectiveDelayMs,
            out double carrierPeriodMs,
            out double delayRatio,
            out decimal? realtimeSpeedMmps) {
            effectiveDelayMs = 0;
            carrierPeriodMs = 0;
            delayRatio = 0;
            realtimeSpeedMmps = null;

            // 步骤1：补偿开关关闭时直接使用固定偏移。
            if (!timingOptions.EnableLoadingMatchTimeCompensation) {
                compensationState = LoadingMatchCompensationState.Fallback;
                fallbackReason = "CompensationDisabled";
                return baseLoadingCarrierId;
            }

            // 步骤2：门禁检查——环未建立。
            if (!_carrierManager.IsRingBuilt) {
                compensationState = LoadingMatchCompensationState.Fallback;
                fallbackReason = "RingNotBuilt";
                _logger.LogWarning(
                    "上车匹配补偿降级 FallbackReason={FallbackReason} ParcelId={ParcelId}",
                    "RingNotBuilt",
                    parcelId);
                return baseLoadingCarrierId;
            }

            // 步骤3：门禁检查——小车总量无效（理论上前置步骤已校验，此处作为防御性检查）。
            if (totalCarrierCount <= 0) {
                compensationState = LoadingMatchCompensationState.Fallback;
                fallbackReason = "InvalidCarrierCount";
                _logger.LogWarning(
                    "上车匹配补偿降级 FallbackReason={FallbackReason} ParcelId={ParcelId}",
                    "InvalidCarrierCount",
                    parcelId);
                return baseLoadingCarrierId;
            }

            // 步骤4：门禁检查——传感器事件通道是否在上次上车后发生过满载丢弃。
            // 丢弃表明触发信号已无法完整传递，补偿无法修复相位跳变，须降级。
            if (Interlocked.Exchange(ref _sensorEventDropSinceLastLoad, 0) != 0) {
                compensationState = LoadingMatchCompensationState.Fallback;
                fallbackReason = "SensorChannelDropWriteExceeded";
                _logger.LogWarning(
                    "上车匹配补偿降级 FallbackReason={FallbackReason} ParcelId={ParcelId}",
                    "SensorChannelDropWriteExceeded",
                    parcelId);
                return baseLoadingCarrierId;
            }

            // 步骤5：门禁检查——获取实时速度并校验合法范围。
            if (!_speedProvider.TryGetSpeedMmps(out var speedMmps)) {
                compensationState = LoadingMatchCompensationState.Fallback;
                fallbackReason = "InvalidRealtimeSpeed";
                _logger.LogWarning(
                    "上车匹配补偿降级 FallbackReason={FallbackReason} ParcelId={ParcelId} 原因=速度不可用",
                    "InvalidRealtimeSpeed",
                    parcelId);
                return baseLoadingCarrierId;
            }

            realtimeSpeedMmps = speedMmps;
            var validMin = timingOptions.RealtimeSpeedValidMinMmps > 0
                ? timingOptions.RealtimeSpeedValidMinMmps
                : SortingTaskTimingOptions.DefaultRealtimeSpeedValidMinMmps;
            var validMax = timingOptions.RealtimeSpeedValidMaxMmps > validMin
                ? timingOptions.RealtimeSpeedValidMaxMmps
                : SortingTaskTimingOptions.DefaultRealtimeSpeedValidMaxMmps;
            if (speedMmps < validMin || speedMmps > validMax) {
                compensationState = LoadingMatchCompensationState.Fallback;
                fallbackReason = "InvalidRealtimeSpeed";
                _logger.LogWarning(
                    "上车匹配补偿降级 FallbackReason={FallbackReason} ParcelId={ParcelId} Speed={Speed} ValidMin={ValidMin} ValidMax={ValidMax}",
                    "InvalidRealtimeSpeed",
                    parcelId,
                    FormatSpeedDecimal(speedMmps),
                    FormatSpeedDecimal(validMin),
                    FormatSpeedDecimal(validMax));
                return baseLoadingCarrierId;
            }

            // 步骤6：校验步距配置并计算单步距时间周期（CarrierPeriodMs = PitchMm / SpeedMmps * 1000）。
            var pitchMm = timingOptions.CarrierPitchMm;
            if (pitchMm <= 0) {
                compensationState = LoadingMatchCompensationState.Fallback;
                fallbackReason = "InvalidCarrierPitchMm";
                _logger.LogWarning(
                    "上车匹配补偿降级 FallbackReason={FallbackReason} ParcelId={ParcelId} CarrierPitchMm={CarrierPitchMm}",
                    "InvalidCarrierPitchMm",
                    parcelId,
                    pitchMm);
                return baseLoadingCarrierId;
            }

            carrierPeriodMs = (double)pitchMm / (double)speedMmps * 1000.0;
            if (carrierPeriodMs <= 0) {
                compensationState = LoadingMatchCompensationState.Fallback;
                fallbackReason = "InvalidCarrierPeriodMs";
                return baseLoadingCarrierId;
            }

            // 步骤7：计算有效延迟（EffectiveDelayMs = changedAt - loadingTriggerOccurredAt）。
            // 无上车触发记录时无法计算延迟，降级为固定偏移。
            if (!_loadingTriggerBoundAtMap.TryGetValue(parcelId, out var loadingTriggerAt)) {
                compensationState = LoadingMatchCompensationState.Fallback;
                fallbackReason = "NoLoadingTriggerRecord";
                return baseLoadingCarrierId;
            }

            effectiveDelayMs = Math.Max(0, (changedAt - loadingTriggerAt).TotalMilliseconds);
            // DelayRatio 转换为百分比与 Enter/Exit 配置口径保持一致。
            delayRatio = carrierPeriodMs > 0 ? effectiveDelayMs / carrierPeriodMs * 100.0 : 0;

            // 步骤8：对延迟占比做可选平滑（窗口>1 时滑动均值，窗口=1 时直接使用原始值）。
            var windowSize = Math.Clamp(timingOptions.LoadingMatchSmoothingWindowSize, 1, MaxSmoothingWindowSize);
            var smoothedDelayRatio = UpdateAndGetSmoothedDelayRatio(delayRatio, windowSize);

            // 步骤9：滞回判定——先平滑后判定 Enter/Exit 门限，防止阈值附近反复翻转。
            var enterPercent = Math.Clamp(timingOptions.LoadingMatchCompensationEnterPercent, 0, 100);
            var exitPercent = Math.Clamp(timingOptions.LoadingMatchCompensationExitPercent, 0, 100);
            if (enterPercent <= exitPercent) {
                // Enter 不大于 Exit 时滞回失去意义，回退默认值以保证门限有效性。
                enterPercent = SortingTaskTimingOptions.DefaultLoadingMatchCompensationEnterPercent;
                exitPercent = SortingTaskTimingOptions.DefaultLoadingMatchCompensationExitPercent;
            }

            var isActive = _compensationHysteresisActive;
            if (!isActive && smoothedDelayRatio >= enterPercent) {
                isActive = true;
            }
            else if (isActive && smoothedDelayRatio < exitPercent) {
                isActive = false;
            }
            _compensationHysteresisActive = isActive;

            if (!isActive) {
                compensationState = LoadingMatchCompensationState.Fallback;
                fallbackReason = "BelowEnterThreshold";
                return baseLoadingCarrierId;
            }

            // 步骤10：应用 delta 偏移得到补偿后小车编号（当前首版仅支持 0 或 +1）。
            compensationState = LoadingMatchCompensationState.Active;
            fallbackReason = string.Empty;
            return ApplyDeltaToCarrierId(baseLoadingCarrierId, timingOptions.LoadingMatchCompensationDelta, totalCarrierCount);
        }

        /// <summary>
        /// 将延迟占比写入平滑窗口并返回滑动均值。
        /// 窗口大小与配置不一致时重建缓冲区（热更新场景）。
        /// </summary>
        /// <param name="rawDelayRatio">本次原始延迟占比（百分比）。</param>
        /// <param name="windowSize">窗口大小（1~20）。</param>
        /// <returns>平滑后的延迟占比（百分比）。</returns>
        private double UpdateAndGetSmoothedDelayRatio(double rawDelayRatio, int windowSize) {
            if (windowSize <= 1) {
                return rawDelayRatio;
            }

            // 步骤：检查缓冲区尺寸是否与配置一致（热更新时可能变化），不一致则重建；
            // 随后将当前占比写入环形指针位置并推进指针，按已填充数量计算滑动均值。
            lock (_smoothingWindowLock) {
                if (_delayRatioWindow.Length != windowSize) {
                    _delayRatioWindow = new double[windowSize];
                    _delayRatioWindowFilled = 0;
                    _delayRatioWindowIndex = 0;
                }

                _delayRatioWindow[_delayRatioWindowIndex] = rawDelayRatio;
                _delayRatioWindowIndex = (_delayRatioWindowIndex + 1) % windowSize;
                if (_delayRatioWindowFilled < windowSize) {
                    _delayRatioWindowFilled++;
                }

                var sum = 0.0;
                for (var i = 0; i < _delayRatioWindowFilled; i++) {
                    sum += _delayRatioWindow[i];
                }

                return sum / _delayRatioWindowFilled;
            }
        }

        /// <summary>
        /// 对基准小车编号应用 delta 偏移（含环绕处理）。
        /// 正数 delta 为顺时针（编号增大方向），负数为逆时针，0 原样返回。
        /// </summary>
        /// <param name="baseCarrierId">基准小车编号（1~totalCarrierCount）。</param>
        /// <param name="delta">偏移量。</param>
        /// <param name="totalCarrierCount">小车总数。</param>
        /// <returns>偏移后的小车编号。</returns>
        private static int ApplyDeltaToCarrierId(int baseCarrierId, int delta, int totalCarrierCount) {
            if (delta == 0) {
                return baseCarrierId;
            }

            return delta > 0
                ? CircularValueHelper.GetClockwiseValue(baseCarrierId, delta, totalCarrierCount)
                : CircularValueHelper.GetCounterClockwiseValue(baseCarrierId, -delta, totalCarrierCount);
        }

        /// <summary>
        /// 将 decimal 速度值格式化为"最多两位小数，整数不带小数位"的字符串（速度字段日志输出规范）。
        /// </summary>
        /// <param name="value">速度值（mm/s）。</param>
        /// <returns>格式化字符串。</returns>
        private static string FormatSpeedDecimal(decimal value) {
            var truncated = decimal.Truncate(value);
            return value == truncated
                ? truncated.ToString("G")
                : Math.Round(value, 2).ToString("G");
        }

        /// <summary>
        /// 将可空 decimal 速度值格式化为字符串；不可用时返回 "N/A"。
        /// </summary>
        /// <param name="value">速度值（mm/s）；null 表示不可用。</param>
        /// <returns>格式化字符串或 "N/A"。</returns>
        private static string FormatSpeedValue(decimal? value) {
            return value.HasValue ? FormatSpeedDecimal(value.Value) : "N/A";
        }

        /// <summary>
        /// 将 double 值格式化为"最多两位小数，整数不带小数位"的字符串（延迟与周期字段日志输出规范）。
        /// </summary>
        /// <param name="value">待格式化的值。</param>
        /// <returns>格式化字符串。</returns>
        private static string FormatDouble(double value) {
            var truncated = Math.Truncate(value);
            return Math.Abs(value - truncated) < DoubleEpsilon
                ? ((long)truncated).ToString()
                : Math.Round(value, 2).ToString("G");
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
            _timingOptionsChangedRegistration.Dispose();
        }
    }
}
