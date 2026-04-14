using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Enums.Sorting;
using Zeye.NarrowBeltSorter.Core.Models.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;
using Zeye.NarrowBeltSorter.Core.Manager.Sorting;
using Zeye.NarrowBeltSorter.Core.Options.Sorting;

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
        /// 上车匹配补偿允许的最大延迟占比（百分比）。
        /// 超出该上限判定为排队等待主导，不进入补偿，避免“危险过补偿”。
        /// </summary>
        private const double MaxCompensationDelayRatioPercent = 180d;

        /// <summary>
        /// 上车命令有界通道容量（条）。
        /// </summary>
        private const int LoadCommandChannelCapacity = 2048;

        private readonly ILogger<SortingTaskCarrierLoadingService> _logger;
        private readonly ICarrierManager _carrierManager;
        private readonly IParcelManager _parcelManager;
        private readonly ILoadingMatchRealtimeSpeedProvider _speedProvider;
        private readonly IOptionsMonitor<SortingTaskTimingOptions> _sortingTaskTimingOptionsMonitor;
        private readonly IDisposable _timingOptionsChangedRegistration;
        private SortingTaskTimingOptions _timingOptionsSnapshot;

        private readonly ConcurrentQueue<ParcelInfo> _readyParcelQueue = new();
        private readonly ConcurrentDictionary<long, long> _carrierParcelMap = new();
        private readonly ConcurrentDictionary<long, byte> _loadingZoneIssuedCarrierMap = new();
        private readonly ConcurrentDictionary<long, long> _loadingReservationMap = new();
        private readonly ConcurrentDictionary<long, byte> _loadingCommandCarrierSet = new();
        private readonly ConcurrentDictionary<long, byte> _loadingCommandParcelSet = new();
        private readonly object _loadingCommandReservationLock = new();
        private readonly ConcurrentDictionary<long, DateTime> _loadingTriggerBoundAtMap = new();
        private readonly ConcurrentDictionary<long, DateTime> _readyQueuedAtMap = new();
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
        /// 延迟占比区间分桶统计（0~50%、50~80%、80~95%、95%+）。
        /// 仅在延迟占比成功计算时（步距周期有效且触发时刻有记录）才累计，用于量化上车匹配时序风险分布。
        /// </summary>
        private readonly DelayRatioIntervalStats _delayRatioIntervalStats = new();

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
        /// 上车命令有序通道（单消费者），用于将慢动作从当前感应位热路径分流。
        /// </summary>
        private readonly Channel<LoadingCommand> _loadCommandChannel =
            Channel.CreateBounded<LoadingCommand>(
                new BoundedChannelOptions(LoadCommandChannelCapacity) {
                    FullMode = BoundedChannelFullMode.DropWrite,
                    SingleReader = true,
                    SingleWriter = false
                });

        /// <summary>
        /// 上车命令通道是否已关闭。
        /// </summary>
        private bool _loadCommandChannelCompleted;

        /// <summary>
        /// 上车命令通道累计丢弃数。
        /// </summary>
        private long _droppedLoadCommandCount;

        /// <summary>
        /// 上车命令通道最近一次丢弃告警时间刻（毫秒）。
        /// </summary>
        private long _lastLoadCommandDropWarningElapsedMs;

        /// <summary>
        /// 上车命令消费者取消源。
        /// </summary>
        private readonly CancellationTokenSource _loadCommandConsumerCts = new();

        /// <summary>
        /// 上车命令消费者任务。
        /// </summary>
        private Task? _loadCommandConsumerTask;

        /// <summary>
        /// 上车命令消费者是否已启动（0=未启动，1=已启动）。
        /// </summary>
        private int _loadCommandConsumerStarted;

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
        /// 上车命令对象：热路径仅完成计算与预占位，慢动作由单消费者执行。
        /// </summary>
        /// <param name="CurrentInductionCarrierId">当前感应位小车编号。</param>
        /// <param name="ChangedAt">当前感应位变化时间。</param>
        /// <param name="ParcelId">预占位包裹编号。</param>
        /// <param name="FinalLoadingCarrierId">最终上车位小车编号。</param>
        /// <param name="BaseLoadingCarrierId">基准上车位小车编号。</param>
        /// <param name="CompensationState">补偿状态。</param>
        /// <param name="FallbackReason">补偿降级原因。</param>
        /// <param name="EffectiveDelayMs">有效延迟毫秒。</param>
        /// <param name="CarrierPeriodMs">步距周期毫秒。</param>
        /// <param name="DelayRatio">延迟占比。</param>
        /// <param name="CompensationSpeedMmps">补偿计算速度。</param>
        /// <param name="TimingOptions">时序配置快照。</param>
        private readonly record struct LoadingCommand(
            long CurrentInductionCarrierId,
            DateTime ChangedAt,
            long ParcelId,
            int FinalLoadingCarrierId,
            int BaseLoadingCarrierId,
            LoadingMatchCompensationState CompensationState,
            string FallbackReason,
            double EffectiveDelayMs,
            double CarrierPeriodMs,
            double DelayRatio,
            decimal? CompensationSpeedMmps,
            SortingTaskTimingOptions TimingOptions);

        /// <summary>
        /// 启动上车命令消费者。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        public Task StartAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _loadCommandConsumerStarted, 1) == 1) {
                return Task.CompletedTask;
            }

            _loadCommandConsumerTask = Task.Run(
                () => ConsumeLoadCommandChannelAsync(_loadCommandConsumerCts.Token),
                _loadCommandConsumerCts.Token);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 停止上车命令消费者并关闭通道。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        public async Task StopAsync(CancellationToken cancellationToken = default) {
            Volatile.Write(ref _loadCommandChannelCompleted, true);
            _loadCommandChannel.Writer.TryComplete();
            _loadCommandConsumerCts.Cancel();
            if (_loadCommandConsumerTask is null) {
                return;
            }

            try {
                await _loadCommandConsumerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // 正常停止路径。
            }
            finally {
                _loadingCommandCarrierSet.Clear();
                _loadingCommandParcelSet.Clear();
                _loadingReservationMap.Clear();
            }
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
            _readyQueuedAtMap.TryAdd(parcel.ParcelId, NormalizeLocalTime(DateTime.Now, "EnqueueReadyParcel", parcel.ParcelId));
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
            _loadingZoneIssuedCarrierMap.Clear();
            _loadingReservationMap.Clear();
            _loadingCommandCarrierSet.Clear();
            _loadingCommandParcelSet.Clear();
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
            _readyQueuedAtMap.TryRemove(parcelId, out _);
            _loadedAtMap.TryRemove(parcelId, out _);
            _arrivedTargetChuteAtMap.TryRemove(parcelId, out _);
        }

        /// <summary>
        /// 清理全部包裹链路时间节点缓存。
        /// </summary>
        public void ClearAllParcelTimelines() {
            _loadingTriggerBoundAtMap.Clear();
            _loadedAtMap.Clear();
            _readyQueuedAtMap.Clear();
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
                // 危险路径隔离：上车位主动装车会回流 LoadStatusChanged(NewIsLoaded) 事件。
                // 若该事件再走“事件驱动出队”分支会造成重复消费与错车风险，故用一次性令牌拦截。
                if (_loadingZoneIssuedCarrierMap.TryRemove(args.CarrierId, out _)) {
                    _logger.LogDebug(
                        "装车事件跳过：由上车位主动装车回流 CarrierId={CarrierId}",
                        args.CarrierId);
                    return;
                }

                // 步骤：新实现中上车仅允许命令消费者写入映射；事件路径不再参与 ReadyQueue 竞争消费，避免双路径竞争。
                if (_carrierParcelMap.ContainsKey(args.CarrierId)) {
                    _logger.LogDebug(
                        "装车事件跳过：小车已在映射中 CarrierId={CarrierId}",
                        args.CarrierId);
                    return;
                }

                _logger.LogDebug(
                    "检测到未由编排命令触发的装车事件，已忽略队列消费以防止双路径竞争 CarrierId={CarrierId}",
                    args.CarrierId);
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
            // 步骤1：快速判空，避免后续所有计算开销。
            cancellationToken.ThrowIfCancellationRequested();
            if (!_readyParcelQueue.TryPeek(out var headParcel)) {
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

            // 步骤3：提前 TryDequeue 获取真实 parcel，用其 ParcelId 参与后续补偿计算，
            // 新实现改为“队头预占位后再由命令消费者出队”，避免“先出队失败再回队尾”扩大 FIFO 漂移。
            var parcelId = headParcel.ParcelId;

            // 步骤4：基于环形偏移计算基准上车位小车编号。
            var baseLoadingCarrierId = CircularValueHelper.GetCounterClockwiseValue(
                currentCarrierValue,
                _carrierManager.LoadingZoneCarrierOffset,
                totalCarrierCount);

            // 步骤5：计算时序补偿，得到最终目标上车位小车编号；同时输出阶段一观测字段。
            var timingOptions = CurrentTimingOptions;
            var finalLoadingCarrierId = ResolveCompensatedLoadingCarrierId(
                baseLoadingCarrierId,
                parcelId,
                changedAt,
                totalCarrierCount,
                timingOptions,
                out var compensationState,
                out var fallbackReason,
                out var effectiveDelayMs,
                out var carrierPeriodMs,
                out var delayRatio,
                out var compensationSpeedMmps);

            // 仅在步距周期有效且已存在有效待装车入队记录时才记录，
            // 避免 NoReadyQueueRecord 早退场景将未成功计算的 delayRatio=0 误计入低占比桶。
            if (carrierPeriodMs > 0 && !string.Equals(fallbackReason, "NoReadyQueueRecord", StringComparison.Ordinal)) {
                _delayRatioIntervalStats.Record(delayRatio);
            }

            if (!_carrierManager.TryGetCarrier(finalLoadingCarrierId, out var loadingCarrier)) {
                _logger.LogWarning("未找到上车位小车，跳过装车 CarrierId={CarrierId}", finalLoadingCarrierId);
                return;
            }

            // 步骤6：上车位已装载时回退包裹并返回，避免重复装车。
            if (loadingCarrier.IsLoaded) {
                return;
            }

            // 步骤7：按“小车+包裹”双重去重并预占位，保证同一目标仅被投递一次。
            lock (_loadingCommandReservationLock) {
                if (!_loadingCommandCarrierSet.TryAdd(finalLoadingCarrierId, 0)) {
                    return;
                }

                if (!_loadingCommandParcelSet.TryAdd(parcelId, 0)) {
                    _loadingCommandCarrierSet.TryRemove(finalLoadingCarrierId, out _);
                    return;
                }

                if (!_loadingReservationMap.TryAdd(finalLoadingCarrierId, parcelId)) {
                    _loadingCommandCarrierSet.TryRemove(finalLoadingCarrierId, out _);
                    _loadingCommandParcelSet.TryRemove(parcelId, out _);
                    return;
                }
            }

            if (_carrierParcelMap.ContainsKey(finalLoadingCarrierId)) {
                _loadingReservationMap.TryRemove(finalLoadingCarrierId, out _);
                _loadingCommandCarrierSet.TryRemove(finalLoadingCarrierId, out _);
                _loadingCommandParcelSet.TryRemove(parcelId, out _);
                _logger.LogWarning(
                    "上车命令投递前发现小车已存在包裹绑定，已取消投递 CarrierId={CarrierId} ParcelId={ParcelId}",
                    finalLoadingCarrierId,
                    parcelId);
                return;
            }

            var command = new LoadingCommand(
                currentInductionCarrierId,
                changedAt,
                parcelId,
                finalLoadingCarrierId,
                baseLoadingCarrierId,
                compensationState,
                fallbackReason,
                effectiveDelayMs,
                carrierPeriodMs,
                delayRatio,
                compensationSpeedMmps,
                timingOptions);
            TryEnqueueLoadingCommand(command);
            await ValueTask.CompletedTask.ConfigureAwait(false);
        }

        /// <summary>
        /// 将上车命令写入有界通道（满载时聚合告警）。
        /// </summary>
        /// <param name="command">上车命令。</param>
        private void TryEnqueueLoadingCommand(LoadingCommand command) {
            if (_loadCommandChannel.Writer.TryWrite(command)) {
                return;
            }

            ReleaseLoadingCommandReservation(command.FinalLoadingCarrierId, command.ParcelId);
            if (Volatile.Read(ref _loadCommandChannelCompleted)) {
                _logger.LogDebug(
                    "上车命令通道已关闭，忽略命令 CarrierId={CarrierId} ParcelId={ParcelId}",
                    command.FinalLoadingCarrierId,
                    command.ParcelId);
                return;
            }

            var dropped = Interlocked.Increment(ref _droppedLoadCommandCount);
            var nowMs = Environment.TickCount64;
            var lastMs = Volatile.Read(ref _lastLoadCommandDropWarningElapsedMs);
            if (unchecked(nowMs - lastMs) >= 1000 &&
                Interlocked.CompareExchange(ref _lastLoadCommandDropWarningElapsedMs, nowMs, lastMs) == lastMs) {
                _logger.LogWarning(
                    "上车命令通道持续满载，已聚合丢弃 DroppedCount={DroppedCount} CarrierId={CarrierId} ParcelId={ParcelId}",
                    dropped,
                    command.FinalLoadingCarrierId,
                    command.ParcelId);
            }
        }

        /// <summary>
        /// 单线程消费上车命令通道，执行真正装车慢动作。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task ConsumeLoadCommandChannelAsync(CancellationToken stoppingToken) {
            await foreach (var command in _loadCommandChannel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false)) {
                try {
                    await ExecuteLoadingCommandAsync(command, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    // 正常取消路径。
                    break;
                }
                catch (Exception ex) {
                    _logger.LogError(
                        ex,
                        "执行上车命令异常 CarrierId={CarrierId} ParcelId={ParcelId}",
                        command.FinalLoadingCarrierId,
                        command.ParcelId);
                }
                finally {
                    ReleaseLoadingCommandReservation(command.FinalLoadingCarrierId, command.ParcelId);
                }
            }
        }

        /// <summary>
        /// 执行单条上车命令（慢动作路径）。
        /// </summary>
        /// <param name="command">上车命令。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task ExecuteLoadingCommandAsync(LoadingCommand command, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_carrierManager.TryGetCarrier(command.FinalLoadingCarrierId, out var loadingCarrier)) {
                _logger.LogWarning(
                    "上车命令执行前校验失败：未找到小车 CarrierId={CarrierId} ParcelId={ParcelId}",
                    command.FinalLoadingCarrierId,
                    command.ParcelId);
                return;
            }

            if (loadingCarrier.IsLoaded) {
                _logger.LogDebug(
                    "上车命令执行前校验失败：小车已装载 CarrierId={CarrierId} ParcelId={ParcelId}",
                    command.FinalLoadingCarrierId,
                    command.ParcelId);
                return;
            }

            if (!_loadingReservationMap.TryGetValue(command.FinalLoadingCarrierId, out var reservedParcelId) ||
                reservedParcelId != command.ParcelId) {
                _logger.LogDebug(
                    "上车命令执行前校验失败：预占位不存在或已变更 CarrierId={CarrierId} ParcelId={ParcelId}",
                    command.FinalLoadingCarrierId,
                    command.ParcelId);
                return;
            }

            if (!_readyParcelQueue.TryDequeue(out var parcel)) {
                _logger.LogDebug(
                    "上车命令执行前校验失败：待装车队列出队失败 ParcelId={ParcelId}",
                    command.ParcelId);
                return;
            }

            if (parcel.ParcelId != command.ParcelId) {
                EnqueueReadyParcel(parcel);
                _logger.LogWarning(
                    "上车命令执行中检测到队头漂移，已回退错位包裹并取消本次命令 ExpectedParcelId={ExpectedParcelId} ActualParcelId={ActualParcelId}",
                    command.ParcelId,
                    parcel.ParcelId);
                return;
            }

            Interlocked.Decrement(ref _readyQueueCount);

            if (!_carrierParcelMap.TryAdd(command.FinalLoadingCarrierId, parcel.ParcelId)) {
                EnqueueReadyParcel(parcel);
                _logger.LogWarning(
                    "上车命令执行前发现小车已存在包裹绑定，已回退包裹 CarrierId={CarrierId} ParcelId={ParcelId}",
                    command.FinalLoadingCarrierId,
                    parcel.ParcelId);
                return;
            }

            _loadingZoneIssuedCarrierMap[command.FinalLoadingCarrierId] = 1;
            var loaded = await loadingCarrier.LoadParcelAsync(parcel, []).ConfigureAwait(false);
            if (!loaded) {
                _loadingZoneIssuedCarrierMap.TryRemove(command.FinalLoadingCarrierId, out _);
                _carrierParcelMap.TryRemove(command.FinalLoadingCarrierId, out _);
                EnqueueReadyParcel(parcel);
                _logger.LogWarning(
                    "调用小车装车失败，已回退映射占位与待装车队列 CarrierId={CarrierId} ParcelId={ParcelId}",
                    command.FinalLoadingCarrierId,
                    parcel.ParcelId);
                return;
            }

            await _parcelManager.BindCarrierAsync(parcel.ParcelId, command.FinalLoadingCarrierId, command.ChangedAt).ConfigureAwait(false);
            RecordLoadedAt(parcel.ParcelId, command.ChangedAt);
            var loadedRawQueueCount = RawQueueCountSnapshot;
            var loadedReadyQueueCount = ReadyQueueCount;
            var loadedInFlightCount = InFlightCarrierParcelCount;
            var loadedDensityBucket = GetDensityBucketLabel(loadedRawQueueCount, loadedReadyQueueCount, loadedInFlightCount);

            if (TryGetElapsedFromTriggerToLoaded(parcel.ParcelId, command.ChangedAt, out var loadedPrevNodeName, out var loadedElapsedFromTrigger, out var loadedElapsedFromTriggerMs)) {
                _triggerToLoadedStats.Record(loadedElapsedFromTriggerMs, loadedDensityBucket);
                _logger.LogInformation(
                    "上车位装车成功 CarrierId={CarrierId} ParcelId={ParcelId} CurrentInductionCarrierId={CurrentInductionCarrierId} BaseLoadingCarrierId={BaseLoadingCarrierId} LoadingZoneOffset={LoadingZoneOffset} Delta={Delta} CompensationState={CompensationState} FallbackReason={FallbackReason} LoopTrackRealtimeSpeedMmps={LoopTrackRealtimeSpeedMmps} CarrierPitchMm={CarrierPitchMm} CarrierPeriodMs={CarrierPeriodMs} EffectiveDelayMs={EffectiveDelayMs} DelayRatio={DelayRatio} [距离{PreviousNodeName}:{ElapsedFromTrigger}] RemainingReadyQueueCount={QueueCount} RawQueueCount={RawQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                    command.FinalLoadingCarrierId,
                    parcel.ParcelId,
                    command.CurrentInductionCarrierId,
                    command.BaseLoadingCarrierId,
                    _carrierManager.LoadingZoneCarrierOffset,
                    command.TimingOptions.LoadingMatchCompensationDelta,
                    command.CompensationState,
                    command.FallbackReason,
                    SortingValueFormatter.FormatSpeed(command.CompensationSpeedMmps),
                    command.TimingOptions.CarrierPitchMm,
                    SortingValueFormatter.FormatDouble(command.CarrierPeriodMs),
                    SortingValueFormatter.FormatDouble(command.EffectiveDelayMs),
                    SortingValueFormatter.FormatDouble(command.DelayRatio),
                    loadedPrevNodeName,
                    loadedElapsedFromTrigger,
                    loadedReadyQueueCount,
                    loadedRawQueueCount,
                    loadedInFlightCount,
                    loadedDensityBucket);

                var alertThresholdMs = ConfigurationValueHelper.GetPositiveOrDefault(
                    command.TimingOptions.ParcelChainAlertThresholdMs,
                    SortingTaskTimingOptions.DefaultParcelChainAlertThresholdMs);
                if (loadedElapsedFromTriggerMs > alertThresholdMs) {
                    _triggerToLoadedStats.RecordExceedance(loadedDensityBucket);
                    _logger.LogWarning(
                        "上车位装车链路耗时超阈值告警 CarrierId={CarrierId} ParcelId={ParcelId} CurrentInductionCarrierId={CurrentInductionCarrierId} BaseLoadingCarrierId={BaseLoadingCarrierId} LoadingZoneOffset={LoadingZoneOffset} Delta={Delta} CompensationState={CompensationState} FallbackReason={FallbackReason} LoopTrackRealtimeSpeedMmps={LoopTrackRealtimeSpeedMmps} CarrierPitchMm={CarrierPitchMm} CarrierPeriodMs={CarrierPeriodMs} EffectiveDelayMs={EffectiveDelayMs} DelayRatio={DelayRatio} PreviousNodeName={PreviousNodeName} ElapsedMs={ElapsedMs} ThresholdMs={ThresholdMs} RawQueueCount={RawQueueCount} ReadyQueueCount={ReadyQueueCount} InFlightCarrierParcelCount={InFlightCarrierParcelCount} DensityBucket={DensityBucket}",
                        command.FinalLoadingCarrierId,
                        parcel.ParcelId,
                        command.CurrentInductionCarrierId,
                        command.BaseLoadingCarrierId,
                        _carrierManager.LoadingZoneCarrierOffset,
                        command.TimingOptions.LoadingMatchCompensationDelta,
                        command.CompensationState,
                        command.FallbackReason,
                        SortingValueFormatter.FormatSpeed(command.CompensationSpeedMmps),
                        command.TimingOptions.CarrierPitchMm,
                        SortingValueFormatter.FormatDouble(command.CarrierPeriodMs),
                        SortingValueFormatter.FormatDouble(command.EffectiveDelayMs),
                        SortingValueFormatter.FormatDouble(command.DelayRatio),
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
                    command.FinalLoadingCarrierId,
                    parcel.ParcelId,
                    command.CurrentInductionCarrierId,
                    command.BaseLoadingCarrierId,
                    _carrierManager.LoadingZoneCarrierOffset,
                    command.TimingOptions.LoadingMatchCompensationDelta,
                    command.CompensationState,
                    command.FallbackReason,
                    SortingValueFormatter.FormatSpeed(command.CompensationSpeedMmps),
                    command.TimingOptions.CarrierPitchMm,
                    SortingValueFormatter.FormatDouble(command.CarrierPeriodMs),
                    SortingValueFormatter.FormatDouble(command.EffectiveDelayMs),
                    SortingValueFormatter.FormatDouble(command.DelayRatio),
                    ReadyQueueCount);
            }
        }

        /// <summary>
        /// 释放命令去重与预占位状态。
        /// </summary>
        /// <param name="carrierId">小车编号。</param>
        /// <param name="parcelId">包裹编号。</param>
        private void ReleaseLoadingCommandReservation(long carrierId, long parcelId) {
            lock (_loadingCommandReservationLock) {
                _loadingReservationMap.TryRemove(carrierId, out _);
                _loadingCommandCarrierSet.TryRemove(carrierId, out _);
                _loadingCommandParcelSet.TryRemove(parcelId, out _);
            }
        }

        /// <summary>
        /// 解析经时序补偿后的最终上车位小车编号，并输出补偿计算的可观测性字段。
        /// </summary>
        /// <param name="baseLoadingCarrierId">基于固定偏移计算的基准上车位小车编号。</param>
        /// <param name="parcelId">待上车包裹编号（用于查找待装车入队时间记录）。</param>
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
                    SortingValueFormatter.FormatSpeed(speedMmps),
                    SortingValueFormatter.FormatSpeed(validMin),
                    SortingValueFormatter.FormatSpeed(validMax));
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
                _logger.LogWarning(
                    "上车匹配补偿降级 FallbackReason={FallbackReason} ParcelId={ParcelId} CarrierPitchMm={CarrierPitchMm} SpeedMmps={SpeedMmps}",
                    "InvalidCarrierPeriodMs",
                    parcelId,
                    pitchMm,
                    SortingValueFormatter.FormatSpeed(speedMmps));
                return baseLoadingCarrierId;
            }
            // 步骤7：计算有效延迟（EffectiveDelayMs = changedAt - readyQueuedAt）。
            // 以“进入待装车队列”作为起点，隔离成熟延迟配置与上车触发绑定路径对补偿的扰动。
            if (!_readyQueuedAtMap.TryGetValue(parcelId, out var readyQueuedAt)) {
                compensationState = LoadingMatchCompensationState.Fallback;
                fallbackReason = "NoReadyQueueRecord";
                _logger.LogWarning(
                    "上车匹配补偿降级 FallbackReason={FallbackReason} ParcelId={ParcelId} 原因=无待装车入队时刻记录",
                    "NoReadyQueueRecord",
                    parcelId);
                return baseLoadingCarrierId;
            }

            var localChangedAt = NormalizeLocalTime(changedAt, "ResolveCompensatedLoadingCarrierId", parcelId);
            var rawEffectiveDelayMs = Math.Max(
                0,
                (localChangedAt - readyQueuedAt).TotalMilliseconds);
            // 步骤7b：将累计等待时间折算到“单步距相位残差”区间 [0, CarrierPeriodMs)。
            // 现场出现 DelayRatio > 100% 的根因是：等待时间跨越了多个步距周期（多圈累计），
            // 但补偿只应响应当前相位误差，而非累计圈数；因此使用取模后的相位延迟参与补偿决策。
            effectiveDelayMs = carrierPeriodMs > 0
                ? rawEffectiveDelayMs % carrierPeriodMs
                : rawEffectiveDelayMs;
            // DelayRatio 转换为百分比与 Enter/Exit 配置口径保持一致（相位延迟口径）。
            delayRatio = carrierPeriodMs > 0 ? effectiveDelayMs / carrierPeriodMs * 100.0 : 0;
            if (rawEffectiveDelayMs >= carrierPeriodMs * 2) {
                _logger.LogDebug(
                    "上车匹配延迟跨多周期 ParcelId={ParcelId} RawEffectiveDelayMs={RawEffectiveDelayMs} PhaseEffectiveDelayMs={PhaseEffectiveDelayMs} CarrierPeriodMs={CarrierPeriodMs}",
                    parcelId,
                    SortingValueFormatter.FormatDouble(rawEffectiveDelayMs),
                    SortingValueFormatter.FormatDouble(effectiveDelayMs),
                    SortingValueFormatter.FormatDouble(carrierPeriodMs));
            }
            if (delayRatio > MaxCompensationDelayRatioPercent) {
                ResetCompensationSmoothingState();
                compensationState = LoadingMatchCompensationState.Fallback;
                fallbackReason = "AboveCompensationRange";
                return baseLoadingCarrierId;
            }

            // 步骤8-9：平滑 + 滞回联合原子执行（同一把锁），防止并发调用导致滞回状态与平滑窗口不一致。
            var windowSize = Math.Clamp(timingOptions.LoadingMatchSmoothingWindowSize, 1, MaxSmoothingWindowSize);
            var enterPercent = Math.Clamp(timingOptions.LoadingMatchCompensationEnterPercent, 0, 100);
            var exitPercent = Math.Clamp(timingOptions.LoadingMatchCompensationExitPercent, 0, 100);
            if (enterPercent <= exitPercent) {
                // Enter 不大于 Exit 时滞回失去意义，回退默认值以保证门限有效性。
                enterPercent = SortingTaskTimingOptions.DefaultLoadingMatchCompensationEnterPercent;
                exitPercent = SortingTaskTimingOptions.DefaultLoadingMatchCompensationExitPercent;
            }

            var isActive = UpdateSmoothedRatioAndHysteresis(delayRatio, windowSize, enterPercent, exitPercent);

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
        /// 将延迟占比写入平滑窗口并在同一把锁内完成滞回状态翻转，返回补偿是否应激活。
        /// 将平滑与滞回合并在一个临界区内执行，保证两者状态对所有并发调用始终一致。
        /// 窗口大小与配置不一致时重建缓冲区（热更新场景）。
        /// </summary>
        /// <param name="rawDelayRatio">本次原始延迟占比（百分比）。</param>
        /// <param name="windowSize">窗口大小（1~MaxSmoothingWindowSize）。</param>
        /// <param name="enterPercent">滞回激活阈值（百分比，须大于 exitPercent）。</param>
        /// <param name="exitPercent">滞回退出阈值（百分比，须小于 enterPercent）。</param>
        /// <returns>补偿是否应激活（true = Active，false = Fallback）。</returns>
        private bool UpdateSmoothedRatioAndHysteresis(
            double rawDelayRatio, int windowSize, int enterPercent, int exitPercent) {
            // 步骤：先平滑，再在同一临界区内翻转滞回状态，保证二者对所有并发调用一致。
            lock (_smoothingWindowLock) {
                double smoothed;
                if (windowSize <= 1) {
                    smoothed = rawDelayRatio;
                }
                else {
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

                    smoothed = sum / _delayRatioWindowFilled;
                }

                if (!_compensationHysteresisActive && smoothed >= enterPercent) {
                    _compensationHysteresisActive = true;
                }
                else if (_compensationHysteresisActive && smoothed < exitPercent) {
                    _compensationHysteresisActive = false;
                }

                return _compensationHysteresisActive;
            }
        }

        /// <summary>
        /// 重置补偿平滑窗口与滞回状态。
        /// 用于隔离异常高延迟占比导致的状态拖尾风险。
        /// </summary>
        private void ResetCompensationSmoothingState() {
            lock (_smoothingWindowLock) {
                _compensationHysteresisActive = false;
                _delayRatioWindowFilled = 0;
                _delayRatioWindowIndex = 0;
                Array.Clear(_delayRatioWindow, 0, _delayRatioWindow.Length);
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
        /// 密度分桶标签数组（静态复用，避免热路径重复分配）。
        /// </summary>
        private static readonly string[] DensityBuckets = ["Low", "Medium", "High"];

        /// <summary>
        /// 输出所有链路阶段 P50/P95/P99 百分位统计日志（按密度分桶分别输出），
        /// 并同步输出延迟占比区间分桶统计，用于量化上车匹配时序风险分布。
        /// </summary>
        private void LogPeriodicLatencyStats() {
            // 步骤1：依次输出四个链路阶段在低/中/高密度分桶下的延迟百分位，便于分析各密度区间下的抖动情况。
            foreach (var bucket in DensityBuckets) {
                LogBucketStats("创建→上车触发", _createdToLoadingTriggerStats, bucket);
                LogBucketStats("触发→上车成功", _triggerToLoadedStats, bucket);
                LogBucketStats("上车成功→到达格口", _loadedToArrivedStats, bucket);
                LogBucketStats("到达格口→落格成功", _arrivedToDroppedStats, bucket);
            }

            // 步骤2：输出延迟占比区间分桶统计，辅助分析高风险占比分布与错位相关性。
            _delayRatioIntervalStats.GetCounts(
                out var count0To50,
                out var count50To80,
                out var count80To95,
                out var count95Plus);
            var totalRatioSamples = count0To50 + count50To80 + count80To95 + count95Plus;
            if (totalRatioSamples > 0) {
                _logger.LogInformation(
                    "延迟占比区间分桶统计 Bucket0To50={Bucket0To50} Bucket50To80={Bucket50To80} Bucket80To95={Bucket80To95} Bucket95Plus={Bucket95Plus} TotalSamples={TotalSamples}",
                    count0To50,
                    count50To80,
                    count80To95,
                    count95Plus,
                    totalRatioSamples);
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
            Volatile.Write(ref _loadCommandChannelCompleted, true);
            _loadCommandChannel.Writer.TryComplete();
            _loadCommandConsumerCts.Cancel();
            _loadCommandConsumerCts.Dispose();
            _timingOptionsChangedRegistration.Dispose();
        }
    }
}
