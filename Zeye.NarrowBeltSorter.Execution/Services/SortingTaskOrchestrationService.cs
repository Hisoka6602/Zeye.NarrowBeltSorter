using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Models.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Sensor;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 分拣任务编排服务：
    /// 1) 包裹创建（由传感器触发）；
    /// 2) 延迟出队后等待装车；
    /// 3) 装车后绑定包裹与小车；
    /// 4) 小车到达目标格口感应位时执行落格。
    /// </summary>
    public sealed class SortingTaskOrchestrationService : BackgroundService {

        /// <summary>
        /// 包裹从创建到进入待装车队列的成熟延迟时间。
        /// </summary>
        private static readonly TimeSpan ParcelMatureDelay = TimeSpan.FromMilliseconds(2000);

        /// <summary>
        /// 格口开门到关门的间隔时间。
        /// </summary>
        private static readonly TimeSpan ChuteOpenCloseInterval = TimeSpan.FromMilliseconds(300);

        /// <summary>
        /// 日志记录器。
        /// </summary>
        private readonly ILogger<SortingTaskOrchestrationService> _logger;

        /// <summary>
        /// 统一安全执行器。
        /// </summary>
        private readonly SafeExecutor _safeExecutor;

        /// <summary>
        /// 小车管理器。
        /// </summary>
        private readonly ICarrierManager _carrierManager;

        /// <summary>
        /// 包裹管理器。
        /// </summary>
        private readonly IParcelManager _parcelManager;

        /// <summary>
        /// 系统状态管理器。
        /// </summary>
        private readonly ISystemStateManager _systemStateManager;

        /// <summary>
        /// 格口管理器。
        /// </summary>
        private readonly IChuteManager _chuteManager;

        /// <summary>
        /// 传感器管理器。
        /// </summary>
        private readonly ISensorManager _sensorManager;

        /// <summary>
        /// 原始包裹队列。
        /// </summary>
        private readonly ConcurrentQueue<ParcelInfo> _rawParcelQueue = new();

        /// <summary>
        /// 待装车包裹队列。
        /// </summary>
        private readonly ConcurrentQueue<ParcelInfo> _readyParcelQueue = new();

        /// <summary>
        /// 小车与已绑定包裹的映射表。
        /// </summary>
        private readonly ConcurrentDictionary<long, long> _carrierParcelMap = new();

        /// <summary>
        /// 原始包裹入队信号量。
        /// </summary>
        private readonly SemaphoreSlim _parcelSignal = new(0);

        /// <summary>
        /// 传感器状态变化事件处理器缓存。
        /// </summary>
        private EventHandler<Core.Events.Io.SensorStateChangedEventArgs>? _sensorStateChangedHandler;

        /// <summary>
        /// 当前感应位小车变化事件处理器缓存。
        /// </summary>
        private EventHandler<Core.Events.Carrier.CurrentInductionCarrierChangedEventArgs>? _currentInductionCarrierChangedHandler;

        /// <summary>
        /// 小车装载状态变化事件处理器缓存。
        /// </summary>
        private EventHandler<Core.Events.Carrier.CarrierLoadStatusChangedEventArgs>? _carrierLoadStatusChangedHandler;

        /// <summary>
        /// 初始化分拣任务编排服务。
        /// </summary>
        /// <param name="logger">日志记录器。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <param name="carrierManager">小车管理器。</param>
        /// <param name="parcelManager">包裹管理器。</param>
        /// <param name="systemStateManager">系统状态管理器。</param>
        /// <param name="chuteManager">格口管理器。</param>
        /// <param name="sensorManager">传感器管理器。</param>
        public SortingTaskOrchestrationService(
            ILogger<SortingTaskOrchestrationService> logger,
            SafeExecutor safeExecutor,
            ICarrierManager carrierManager,
            IParcelManager parcelManager,
            ISystemStateManager systemStateManager,
            IChuteManager chuteManager,
            ISensorManager sensorManager) {
            // 步骤 1：校验所有依赖，确保服务启动前依赖完整。
            logger.LogInformation("分拣编排步骤 Step={StepDescription}", "步骤 1：校验所有依赖，确保服务启动前依赖完整。");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _carrierManager = carrierManager ?? throw new ArgumentNullException(nameof(carrierManager));
            _parcelManager = parcelManager ?? throw new ArgumentNullException(nameof(parcelManager));
            _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));
            _chuteManager = chuteManager ?? throw new ArgumentNullException(nameof(chuteManager));
            _sensorManager = sensorManager ?? throw new ArgumentNullException(nameof(sensorManager));
        }

        /// <summary>
        /// 执行后台服务主循环。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            // 步骤 1：启动时先订阅全部业务事件。
            LogStep("步骤 1：启动时先订阅全部业务事件。");
            SubscribeEvents();

            try {
                // 步骤 2：持续泵送原始包裹队列，将成熟包裹转移到待装车队列。
                LogStep("步骤 2：持续泵送原始包裹队列，将成熟包裹转移到待装车队列。");
                await PumpRawQueueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // 步骤 3：主机停止时走正常取消路径，不视为异常。
                LogStep("步骤 3：主机停止时走正常取消路径，不视为异常。");
            }
            finally {
                // 步骤 4：退出前统一取消事件订阅，避免宿主重复启动时残留订阅。
                LogStep("步骤 4：退出前统一取消事件订阅，避免宿主重复启动时残留订阅。");
                UnsubscribeEvents();
            }
        }

        /// <summary>
        /// 停止后台服务。
        /// </summary>
        /// <param name="cancellationToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        public override async Task StopAsync(CancellationToken cancellationToken) {
            // 步骤 1：先取消事件订阅，阻止新的业务事件继续进入。
            LogStep("步骤 1：先取消事件订阅，阻止新的业务事件继续进入。");
            UnsubscribeEvents();

            // 步骤 2：释放一次信号量，唤醒可能正在等待的队列泵送循环。
            LogStep("步骤 2：释放一次信号量，唤醒可能正在等待的队列泵送循环。");
            _parcelSignal.Release();

            // 步骤 3：继续执行基类停止流程。
            LogStep("步骤 3：继续执行基类停止流程。");
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 订阅业务事件。
        /// </summary>
        private void SubscribeEvents() {
            // 步骤 1：初始化事件处理器缓存，确保同一实例只绑定一次。
            LogStep("步骤 1：初始化事件处理器缓存，确保同一实例只绑定一次。");
            _sensorStateChangedHandler ??= OnSensorStateChanged;
            _currentInductionCarrierChangedHandler ??= OnCurrentInductionCarrierChanged;
            _carrierLoadStatusChangedHandler ??= OnCarrierLoadStatusChanged;

            // 步骤 2：订阅传感器变化、小车感应位变化、装载状态变化事件。
            LogStep("步骤 2：订阅传感器变化、小车感应位变化、装载状态变化事件。");
            _sensorManager.SensorStateChanged += _sensorStateChangedHandler;
            _carrierManager.CurrentInductionCarrierChanged += _currentInductionCarrierChangedHandler;
            _carrierManager.CarrierLoadStatusChanged += _carrierLoadStatusChangedHandler;
        }

        /// <summary>
        /// 取消订阅业务事件。
        /// </summary>
        private void UnsubscribeEvents() {
            // 步骤 1：若传感器处理器已绑定，则解除订阅。
            LogStep("步骤 1：若传感器处理器已绑定，则解除订阅。");
            if (_sensorStateChangedHandler is not null) {
                _sensorManager.SensorStateChanged -= _sensorStateChangedHandler;
            }

            // 步骤 2：若当前感应位小车处理器已绑定，则解除订阅。
            LogStep("步骤 2：若当前感应位小车处理器已绑定，则解除订阅。");
            if (_currentInductionCarrierChangedHandler is not null) {
                _carrierManager.CurrentInductionCarrierChanged -= _currentInductionCarrierChangedHandler;
            }

            // 步骤 3：若装载状态处理器已绑定，则解除订阅。
            LogStep("步骤 3：若装载状态处理器已绑定，则解除订阅。");
            if (_carrierLoadStatusChangedHandler is not null) {
                _carrierManager.CarrierLoadStatusChanged -= _carrierLoadStatusChangedHandler;
            }

            // 步骤 4：清空处理器缓存，防止后续误用旧引用。
            LogStep("步骤 4：清空处理器缓存，防止后续误用旧引用。");
            _sensorStateChangedHandler = null;
            _currentInductionCarrierChangedHandler = null;
            _carrierLoadStatusChangedHandler = null;
        }

        /// <summary>
        /// 将原始包裹队列中的包裹按成熟时间转移到待装车队列。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        private async Task PumpRawQueueAsync(CancellationToken stoppingToken) {
            // 步骤 1：只要服务未停止，就持续处理原始包裹队列。
            LogStep("步骤 1：只要服务未停止，就持续处理原始包裹队列。");
            while (!stoppingToken.IsCancellationRequested) {
                // 步骤 2：等待新的原始包裹入队信号。
                LogStep("步骤 2：等待新的原始包裹入队信号。");
                await _parcelSignal.WaitAsync(stoppingToken).ConfigureAwait(false);

                // 步骤 3：批量处理当前原始队列中已存在的包裹。
                LogStep("步骤 3：批量处理当前原始队列中已存在的包裹。");
                while (_rawParcelQueue.TryPeek(out var headParcel)) {
                    // 步骤 4：计算队首包裹的成熟时间。
                    LogStep("步骤 4：计算队首包裹的成熟时间。");
                    var now = DateTime.Now;
                    var matureAt = GetParcelMatureAt(headParcel.ParcelId);

                    // 步骤 5：若包裹尚未成熟，则等待到成熟时刻。
                    LogStep("步骤 5：若包裹尚未成熟，则等待到成熟时刻。");
                    if (matureAt > now) {
                        var waitDuration = matureAt - now;
                        await Task.Delay(waitDuration, stoppingToken).ConfigureAwait(false);
                    }

                    // 步骤 6：队首包裹成熟后正式出队并转入待装车队列。
                    LogStep("步骤 6：队首包裹成熟后正式出队并转入待装车队列。");
                    if (_rawParcelQueue.TryDequeue(out var readyParcel)) {
                        _readyParcelQueue.Enqueue(readyParcel);

                        // 步骤 7：记录转移结果，便于跟踪待装车队列长度变化。
                        LogStep("步骤 7：记录转移结果，便于跟踪待装车队列长度变化。");
                        _logger.LogInformation(
                            "包裹进入待装车队列 ParcelId={ParcelId} ReadyQueueCount={QueueCount}",
                            readyParcel.ParcelId,
                            _readyParcelQueue.Count);
                    }
                }
            }
        }

        /// <summary>
        /// 处理传感器状态变化事件。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="args">事件参数。</param>
        private void OnSensorStateChanged(object? sender, Core.Events.Io.SensorStateChangedEventArgs args) {
            _ = _safeExecutor.ExecuteAsync(
                () => HandleSensorStateChangedAsync(args),
                "SortingTaskOrchestrationService.OnSensorStateChanged");
        }

        /// <summary>
        /// 处理小车装载状态变化事件。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="args">事件参数。</param>
        private void OnCarrierLoadStatusChanged(object? sender, Core.Events.Carrier.CarrierLoadStatusChangedEventArgs args) {
            _ = _safeExecutor.ExecuteAsync(
                () => HandleCarrierLoadStatusChangedAsync(args),
                "SortingTaskOrchestrationService.OnCarrierLoadStatusChanged");
        }

        /// <summary>
        /// 处理当前感应位小车变化事件。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="args">事件参数。</param>
        private void OnCurrentInductionCarrierChanged(object? sender, Core.Events.Carrier.CurrentInductionCarrierChangedEventArgs args) {
            _ = _safeExecutor.ExecuteAsync(
                () => HandleCurrentInductionCarrierChangedAsync(args),
                "SortingTaskOrchestrationService.OnCurrentInductionCarrierChanged");
        }

        /// <summary>
        /// 安全执行传感器状态变化业务逻辑。
        /// </summary>
        /// <param name="args">传感器状态变化事件参数。</param>
        /// <returns>异步任务。</returns>
        private async Task HandleSensorStateChangedAsync(Core.Events.Io.SensorStateChangedEventArgs args) {
            // 步骤 1：仅处理包裹创建传感器的有效触发边沿。
            LogStep("步骤 1：仅处理包裹创建传感器的有效触发边沿。");
            if (args.SensorType != IoPointType.ParcelCreateSensor || args.NewState != args.TriggerState ||
                !_carrierManager.IsRingBuilt) {
                return;
            }

            // 步骤 2：仅在系统运行态下允许创建包裹。
            LogStep("步骤 2：仅在系统运行态下允许创建包裹。");
            if (_systemStateManager.CurrentState != SystemState.Running) {
                return;
            }

            // 步骤 3：生成包裹编号并计算目标格口。
            LogStep("步骤 3：生成包裹编号并计算目标格口。");
            var parcelId = DateTime.Now.Ticks;
            var parcel = new ParcelInfo {
                ParcelId = parcelId,
            };

            // 步骤 4：先写入包裹管理器，确保包裹实体已注册。
            LogStep("步骤 4：先写入包裹管理器，确保包裹实体已注册。");
            var created = await _parcelManager.CreateAsync(parcel).ConfigureAwait(false);
            if (!created) {
                _logger.LogWarning("创建包裹失败或包裹已存在 ParcelId={ParcelId}", parcelId);
                return;
            }

            // 步骤 5：将包裹放入原始队列，并唤醒成熟泵送流程。
            LogStep("步骤 5：将包裹放入原始队列，并唤醒成熟泵送流程。");
            _rawParcelQueue.Enqueue(parcel);
            _parcelSignal.Release();

            // 步骤 6：记录创建完成日志。
            LogStep("步骤 6：记录创建完成日志。");
            _logger.LogInformation("创建包裹成功并入原始队列 ParcelId={ParcelId}", parcelId);
        }

        /// <summary>
        /// 安全执行小车装载状态变化业务逻辑。
        /// </summary>
        /// <param name="args">小车装载状态变化事件参数。</param>
        /// <returns>异步任务。</returns>
        private async Task HandleCarrierLoadStatusChangedAsync(Core.Events.Carrier.CarrierLoadStatusChangedEventArgs args) {
            // 步骤 1：仅在系统运行态下处理装卸货事件。
            LogStep("步骤 1：仅在系统运行态下处理装卸货事件。");
            if (_systemStateManager.CurrentState != SystemState.Running) {
                return;
            }

            // 步骤 2：若为装车事件，则从待装车队列中取出一个包裹进行绑定。
            LogStep("步骤 2：若为装车事件，则从待装车队列中取出一个包裹进行绑定。");
            if (args.NewIsLoaded) {
                // 步骤 2.0：若已存在绑定映射，说明装车已在其他流程完成，避免重复消费待装车队列。
                LogStep("步骤 2.0：若已存在绑定映射，说明装车已在其他流程完成，避免重复消费待装车队列。");
                if (_carrierParcelMap.ContainsKey(args.CarrierId)) {
                    return;
                }
                if (_readyParcelQueue.TryDequeue(out var parcel)) {
                    // 步骤 2.1：建立小车与包裹的内存映射关系。
                    LogStep("步骤 2.1：建立小车与包裹的内存映射关系。");
                    _carrierParcelMap[args.CarrierId] = parcel.ParcelId;

                    // 步骤 2.2：同步写入包裹绑定状态。
                    LogStep("步骤 2.2：同步写入包裹绑定状态。");
                    await _parcelManager.BindCarrierAsync(
                        parcel.ParcelId,
                        args.CarrierId,
                        DateTime.Now).ConfigureAwait(false);

                    // 步骤 2.3：记录装车成功日志。
                    LogStep("步骤 2.3：记录装车成功日志。");
                    _logger.LogInformation(
                        "装车成功 CarrierId={CarrierId} ParcelId={ParcelId} RemainingReadyQueueCount={QueueCount}",
                        args.CarrierId,
                        parcel.ParcelId,
                        _readyParcelQueue.Count);
                }
                else {
                    // 步骤 2.4：若待装车队列为空，则记录告警日志。
                    LogStep("步骤 2.4：若待装车队列为空，则记录告警日志。");
                    _logger.LogWarning("装车事件到达但待装车队列为空 CarrierId={CarrierId}", args.CarrierId);
                }

                return;
            }

            // 步骤 3：若为卸货事件，则尝试移除已有绑定关系。
            LogStep("步骤 3：若为卸货事件，则尝试移除已有绑定关系。");
            if (_carrierParcelMap.TryRemove(args.CarrierId, out var oldParcelId)) {
                // 步骤 3.1：同步写入包裹解绑状态。
                LogStep("步骤 3.1：同步写入包裹解绑状态。");
                await _parcelManager.UnbindCarrierAsync(
                    oldParcelId,
                    args.CarrierId,
                    DateTime.Now).ConfigureAwait(false);

                // 步骤 3.2：记录解绑结果。
                LogStep("步骤 3.2：记录解绑结果。");
                _logger.LogInformation(
                    "卸货事件触发解绑 CarrierId={CarrierId} ParcelId={ParcelId}",
                    args.CarrierId,
                    oldParcelId);
            }
        }

        /// <summary>
        /// 安全执行当前感应位小车变化业务逻辑。
        /// </summary>
        /// <param name="args">当前感应位小车变化事件参数。</param>
        /// <returns>异步任务。</returns>
        private async Task HandleCurrentInductionCarrierChangedAsync(
            Core.Events.Carrier.CurrentInductionCarrierChangedEventArgs args) {
            // 步骤 1：仅在系统运行态且当前感应位存在小车时继续处理。
            LogStep("步骤 1：仅在系统运行态且当前感应位存在小车时继续处理。");
            if (_systemStateManager.CurrentState != SystemState.Running || !args.NewCarrierId.HasValue) {
                return;
            }

            // 步骤 2.1：获取稳定排序后的小车编号数组，用于偏移映射。
            LogStep("步骤 2.1：获取稳定排序后的小车编号数组，用于偏移映射。");
            // 步骤 2：尝试在上车位执行装车，确保后续落格映射可以获取到包裹绑定关系。
            LogStep("步骤 2：尝试在上车位执行装车，确保后续落格映射可以获取到包裹绑定关系。");
            await TryLoadParcelAtLoadingZoneAsync(args.NewCarrierId.Value).ConfigureAwait(false);
            var orderedCarrierIds = GetOrderedCarrierIds();
            if (orderedCarrierIds.Length == 0) {
                return;
            }

            // 步骤 3：遍历每个格口对应的偏移映射关系。
            LogStep("步骤 3：遍历每个格口对应的偏移映射关系。");
            foreach (var pair in _carrierManager.ChuteCarrierOffsetMap) {
                var chuteId = pair.Key;

                // 步骤 4：根据当前感应位小车和格口偏移量，解析当前位于格口前的小车编号。
                LogStep("步骤 4：根据当前感应位小车和格口偏移量，解析当前位于格口前的小车编号。");
                var carrierIdAtChute = ResolveCarrierIdAtChute(
                    args.NewCarrierId.Value,
                    pair.Value,
                    orderedCarrierIds);

                if (!carrierIdAtChute.HasValue) {
                    continue;
                }

                // 步骤 5：检查该小车上是否存在已绑定包裹。
                LogStep("步骤 5：检查该小车上是否存在已绑定包裹。");
                if (!_carrierParcelMap.TryGetValue(carrierIdAtChute.Value, out var parcelId)) {
                    continue;
                }

                // 步骤 6：检查包裹是否存在且目标格口与当前格口一致。
                LogStep("步骤 6：检查包裹是否存在且目标格口与当前格口一致。");
                if (!_parcelManager.TryGet(parcelId, out var parcel) || parcel.TargetChuteId != chuteId) {
                    continue;
                }

                // 步骤 7：获取格口实例，用于执行实际落格动作。
                LogStep("步骤 7：获取格口实例，用于执行实际落格动作。");
                if (!_chuteManager.TryGetChute(chuteId, out var chute)) {
                    _logger.LogWarning("落格失败，未找到格口 ChuteId={ChuteId}", chuteId);
                    continue;
                }

                // 步骤 8：调用格口执行开门、落格、关门流程。
                LogStep("步骤 8：调用格口执行开门、落格、关门流程。");
                var droppedAt = DateTime.Now;
                var dropped = await chute.DropAsync(parcel, droppedAt, ChuteOpenCloseInterval).ConfigureAwait(false);
                if (!dropped) {
                    _logger.LogWarning(
                        "落格调用返回失败 ChuteId={ChuteId} CarrierId={CarrierId} ParcelId={ParcelId}",
                        chuteId,
                        carrierIdAtChute.Value,
                        parcelId);
                    continue;
                }

                // 步骤 9：落格成功后，更新包裹落格状态并解除小车绑定。
                LogStep("步骤 9：落格成功后，更新包裹落格状态并解除小车绑定。");
                await _parcelManager.MarkDroppedAsync(parcelId, chuteId, droppedAt).ConfigureAwait(false);
                await _parcelManager.UnbindCarrierAsync(parcelId, carrierIdAtChute.Value, DateTime.Now).ConfigureAwait(false);
                _carrierParcelMap.TryRemove(carrierIdAtChute.Value, out _);

                // 步骤 10：记录落格成功日志。
                LogStep("步骤 10：记录落格成功日志。");
                _logger.LogInformation(
                    "落格成功 ChuteId={ChuteId} CarrierId={CarrierId} ParcelId={ParcelId}",
                    chuteId,
                    carrierIdAtChute.Value,
                    parcelId);
            }
        }

        /// <summary>
        /// 在上车位根据当前感应位小车和上车位偏移尝试装车。
        /// </summary>
        /// <param name="currentInductionCarrierId">当前感应位小车编号。</param>
        /// <returns>异步任务。</returns>
        private async Task TryLoadParcelAtLoadingZoneAsync(long currentInductionCarrierId) {
            // 步骤 1：无待装车包裹时无需继续处理。
            LogStep("步骤 1：无待装车包裹时无需继续处理。");
            if (!_readyParcelQueue.TryPeek(out _)) {
                return;
            }

            // 步骤 2：根据环形偏移计算上车位小车编号。
            LogStep("步骤 2：根据环形偏移计算上车位小车编号。");
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

            // 步骤 3：获取上车位小车实例。
            LogStep("步骤 3：获取上车位小车实例。");
            if (!_carrierManager.TryGetCarrier(loadingCarrierId, out var loadingCarrier)) {
                _logger.LogWarning("未找到上车位小车，跳过装车 CarrierId={CarrierId}", loadingCarrierId);
                return;
            }

            // 步骤 4：上车位已有包裹时不重复装车。
            LogStep("步骤 4：上车位已有包裹时不重复装车。");
            if (loadingCarrier.IsLoaded) {
                return;
            }

            // 步骤 5：出队一个成熟包裹并触发装车。
            LogStep("步骤 5：出队一个成熟包裹并触发装车。");
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

            // 步骤 6：装车成功后立即写入内存映射和包裹绑定状态。
            LogStep("步骤 6：装车成功后立即写入内存映射和包裹绑定状态。");
            _carrierParcelMap[loadingCarrierId] = parcel.ParcelId;
            await _parcelManager.BindCarrierAsync(parcel.ParcelId, loadingCarrierId, DateTime.Now).ConfigureAwait(false);

            _logger.LogInformation(
                "上车位装车成功 CarrierId={CarrierId} ParcelId={ParcelId} RemainingReadyQueueCount={QueueCount}",
                loadingCarrierId,
                parcel.ParcelId,
                _readyParcelQueue.Count);
        }

        /// <summary>
        /// 获取按小车编号升序排列的小车编号数组。
        /// </summary>
        /// <remarks>
        /// 步骤：
        /// 1. 检查环路是否已构建以及小车集合是否存在有效数据；
        /// 2. 若当前不满足计算条件，则直接返回空数组；
        /// 3. 从小车集合中提取小车编号；
        /// 4. 按编号升序排序；
        /// 5. 将结果转换为数组并返回。
        /// </remarks>
        /// <returns>按编号升序排列的小车编号数组。</returns>
        private long[] GetOrderedCarrierIds() {
            // 步骤 1：环路未构建或当前不存在任何小车时，直接返回空数组，避免无意义枚举。
            LogStep("步骤 1：环路未构建或当前不存在任何小车时，直接返回空数组，避免无意义枚举。");
            if (!_carrierManager.IsRingBuilt || _carrierManager.Carriers.Count == 0) {
                return [];
            }

            // 步骤 2：提取所有小车编号。
            LogStep("步骤 2：提取所有小车编号。");
            // 步骤 3：按照小车编号升序排序，确保后续偏移映射计算稳定一致。
            LogStep("步骤 3：按照小车编号升序排序，确保后续偏移映射计算稳定一致。");
            // 步骤 4：转换为数组，供调用方进行高效索引访问。
            LogStep("步骤 4：转换为数组，供调用方进行高效索引访问。");
            return _carrierManager.Carriers
                .Select(x => x.Id)
                .OrderBy(x => x)
                .ToArray();
        }

        /// <summary>
        /// 根据当前感应位小车和格口偏移量，解析位于目标格口前的小车编号。
        /// </summary>
        /// <param name="currentInductionCarrierId">当前感应位小车编号。</param>
        /// <param name="chuteOffset">格口相对偏移量。</param>
        /// <param name="orderedCarrierIds">按编号排序后的小车编号集合。</param>
        /// <returns>目标小车编号；若无法解析则返回空。</returns>
        private long? ResolveCarrierIdAtChute(
            long currentInductionCarrierId,
            int chuteOffset,
            IReadOnlyList<long> orderedCarrierIds) {
            // 步骤 1：在线性数组中查找当前感应位小车的索引位置。
            LogStep("步骤 1：在线性数组中查找当前感应位小车的索引位置。");
            var currentIndex = -1;
            for (var i = 0; i < orderedCarrierIds.Count; i++) {
                if (orderedCarrierIds[i] == currentInductionCarrierId) {
                    currentIndex = i;
                    break;
                }
            }

            // 步骤 2：若未找到当前小车，则无法完成映射，直接返回空。
            LogStep("步骤 2：若未找到当前小车，则无法完成映射，直接返回空。");
            if (currentIndex < 0) {
                return null;
            }

            // 步骤 3：叠加格口偏移量，并通过环形索引修正落在有效区间内。
            LogStep("步骤 3：叠加格口偏移量，并通过环形索引修正落在有效区间内。");
            var mappedIndex = WrapIndex(currentIndex + chuteOffset, orderedCarrierIds.Count);

            // 步骤 4：返回偏移后对应的小车编号。
            LogStep("步骤 4：返回偏移后对应的小车编号。");
            return orderedCarrierIds[mappedIndex];
        }

        /// <summary>
        /// 根据包裹编号计算包裹成熟时间。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <returns>包裹成熟时间。</returns>
        private DateTime GetParcelMatureAt(long parcelId) {
            // 步骤 1：将基于本地时间生成的包裹编号还原为创建时间。
            LogStep("步骤 1：将基于本地时间生成的包裹编号还原为创建时间。");
            var createdAt = new DateTime(parcelId, DateTimeKind.Local);

            // 步骤 2：叠加成熟延迟，得到包裹可进入待装车队列的时间点。
            LogStep("步骤 2：叠加成熟延迟，得到包裹可进入待装车队列的时间点。");
            return createdAt + ParcelMatureDelay;
        }

        /// <summary>
        /// 记录分拣编排步骤日志。
        /// </summary>
        /// <param name="stepDescription">步骤描述。</param>
        private void LogStep(string stepDescription) {
            _logger.LogInformation("分拣编排步骤 Step={StepDescription}", stepDescription);
        }

        /// <summary>
        /// 将任意索引映射到指定长度的环形区间内。
        /// </summary>
        /// <param name="index">原始索引。</param>
        /// <param name="length">环形区间长度。</param>
        /// <returns>修正后的有效索引。</returns>
        private int WrapIndex(int index, int length) {
            // 步骤 1：长度非法时直接返回 0，避免除零错误。
            LogStep("步骤 1：长度非法时直接返回 0，避免除零错误。");
            if (length <= 0) {
                return 0;
            }

            // 步骤 2：先执行取模运算。
            LogStep("步骤 2：先执行取模运算。");
            var result = index % length;

            // 步骤 3：若结果为负数，则补回到有效正区间。
            LogStep("步骤 3：若结果为负数，则补回到有效正区间。");
            return result < 0 ? result + length : result;
        }
    }
}
