using System;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Models.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Sensor;
using Zeye.NarrowBeltSorter.Core.Manager.System;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 分拣任务编排服务（主协调器）：
    /// 1) 包裹创建与成熟泵送；
    /// 2) 上车编排委托给 SortingTaskCarrierLoadingService；
    /// 3) 落格编排委托给 SortingTaskDropOrchestrationService。
    /// </summary>
    public sealed class SortingTaskOrchestrationService : BackgroundService {

        /// <summary>
        /// 包裹从创建到进入待装车队列的成熟延迟时间。
        /// </summary>
        private static readonly TimeSpan ParcelMatureDelay = TimeSpan.FromMilliseconds(2000);

        /// <summary>
        /// 日志记录器。
        /// </summary>
        private readonly ILogger<SortingTaskOrchestrationService> _logger;

        /// <summary>
        /// 统一安全执行器。
        /// </summary>
        private readonly SafeExecutor _safeExecutor;

        /// <summary>
        /// 包裹管理器。
        /// </summary>
        private readonly IParcelManager _parcelManager;

        /// <summary>
        /// 系统状态管理器。
        /// </summary>
        private readonly ISystemStateManager _systemStateManager;

        /// <summary>
        /// 传感器管理器。
        /// </summary>
        private readonly ISensorManager _sensorManager;

        /// <summary>
        /// 上车编排服务。
        /// </summary>
        private readonly SortingTaskCarrierLoadingService _carrierLoadingService;

        /// <summary>
        /// 落格编排服务。
        /// </summary>
        private readonly SortingTaskDropOrchestrationService _dropOrchestrationService;

        /// <summary>
        /// 原始包裹队列。
        /// </summary>
        private readonly ConcurrentQueue<ParcelInfo> _rawParcelQueue = new();

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
        public SortingTaskOrchestrationService(
            ILogger<SortingTaskOrchestrationService> logger,
            SafeExecutor safeExecutor,
            IParcelManager parcelManager,
            ISystemStateManager systemStateManager,
            ISensorManager sensorManager,
            SortingTaskCarrierLoadingService carrierLoadingService,
            SortingTaskDropOrchestrationService dropOrchestrationService) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _parcelManager = parcelManager ?? throw new ArgumentNullException(nameof(parcelManager));
            _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));
            _sensorManager = sensorManager ?? throw new ArgumentNullException(nameof(sensorManager));
            _carrierLoadingService = carrierLoadingService ?? throw new ArgumentNullException(nameof(carrierLoadingService));
            _dropOrchestrationService = dropOrchestrationService ?? throw new ArgumentNullException(nameof(dropOrchestrationService));
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            SubscribeEvents();

            try {
                await PumpRawQueueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // 宿主正常停止。
            }
            finally {
                UnsubscribeEvents();
            }
        }

        /// <inheritdoc />
        public override async Task StopAsync(CancellationToken cancellationToken) {
            UnsubscribeEvents();
            _parcelSignal.Release();
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 订阅业务事件。
        /// </summary>
        private void SubscribeEvents() {
            _sensorStateChangedHandler ??= OnSensorStateChanged;
            _currentInductionCarrierChangedHandler ??= OnCurrentInductionCarrierChanged;
            _carrierLoadStatusChangedHandler ??= OnCarrierLoadStatusChanged;

            _sensorManager.SensorStateChanged += _sensorStateChangedHandler;
            _carrierLoadingService.CurrentInductionCarrierChanged += _currentInductionCarrierChangedHandler;
            _carrierLoadingService.CarrierLoadStatusChanged += _carrierLoadStatusChangedHandler;
        }

        /// <summary>
        /// 取消订阅业务事件。
        /// </summary>
        private void UnsubscribeEvents() {
            if (_sensorStateChangedHandler is not null) {
                _sensorManager.SensorStateChanged -= _sensorStateChangedHandler;
            }

            if (_currentInductionCarrierChangedHandler is not null) {
                _carrierLoadingService.CurrentInductionCarrierChanged -= _currentInductionCarrierChangedHandler;
            }

            if (_carrierLoadStatusChangedHandler is not null) {
                _carrierLoadingService.CarrierLoadStatusChanged -= _carrierLoadStatusChangedHandler;
            }

            _sensorStateChangedHandler = null;
            _currentInductionCarrierChangedHandler = null;
            _carrierLoadStatusChangedHandler = null;
        }

        /// <summary>
        /// 将原始包裹队列中的包裹按成熟时间转移到待装车队列。
        /// </summary>
        private async Task PumpRawQueueAsync(CancellationToken stoppingToken) {
            while (!stoppingToken.IsCancellationRequested) {
                await _parcelSignal.WaitAsync(stoppingToken).ConfigureAwait(false);

                while (_rawParcelQueue.TryPeek(out var headParcel)) {
                    var now = DateTime.Now;
                    var matureAt = GetParcelMatureAt(headParcel.ParcelId);
                    if (matureAt > now) {
                        await Task.Delay(matureAt - now, stoppingToken).ConfigureAwait(false);
                    }

                    if (_rawParcelQueue.TryDequeue(out var readyParcel)) {
                        _carrierLoadingService.EnqueueReadyParcel(readyParcel);
                        _logger.LogInformation(
                            "包裹进入待装车队列 ParcelId={ParcelId} ReadyQueueCount={QueueCount}",
                            readyParcel.ParcelId,
                            _carrierLoadingService.ReadyQueueCount);
                    }
                }
            }
        }

        /// <summary>
        /// 处理传感器状态变化事件。
        /// </summary>
        private void OnSensorStateChanged(object? sender, Core.Events.Io.SensorStateChangedEventArgs args) {
            _ = _safeExecutor.ExecuteAsync(
                () => HandleSensorStateChangedAsync(args),
                "SortingTaskOrchestrationService.OnSensorStateChanged");
        }

        /// <summary>
        /// 处理小车装载状态变化事件。
        /// </summary>
        private void OnCarrierLoadStatusChanged(object? sender, Core.Events.Carrier.CarrierLoadStatusChangedEventArgs args) {
            var currentState = _systemStateManager.CurrentState;
            _ = _safeExecutor.ExecuteAsync(
                token => _carrierLoadingService.HandleCarrierLoadStatusChangedAsync(args, currentState, token),
                "SortingTaskOrchestrationService.OnCarrierLoadStatusChanged");
        }

        /// <summary>
        /// 处理当前感应位小车变化事件。
        /// </summary>
        private void OnCurrentInductionCarrierChanged(object? sender, Core.Events.Carrier.CurrentInductionCarrierChangedEventArgs args) {
            var currentState = _systemStateManager.CurrentState;
            _ = _safeExecutor.ExecuteAsync(
                token => _dropOrchestrationService.HandleCurrentInductionCarrierChangedAsync(args, currentState, token),
                "SortingTaskOrchestrationService.OnCurrentInductionCarrierChanged");
        }

        /// <summary>
        /// 安全执行传感器状态变化业务逻辑。
        /// </summary>
        private async Task HandleSensorStateChangedAsync(Core.Events.Io.SensorStateChangedEventArgs args) {
            if (args.SensorType != IoPointType.ParcelCreateSensor || args.NewState != args.TriggerState) {
                return;
            }

            if (_systemStateManager.CurrentState != SystemState.Running) {
                return;
            }

            var parcelId = DateTime.Now.Ticks;
            var parcel = new ParcelInfo {
                ParcelId = parcelId,
            };

            var created = await _parcelManager.CreateAsync(parcel).ConfigureAwait(false);
            if (!created) {
                _logger.LogWarning("创建包裹失败或包裹已存在 ParcelId={ParcelId}", parcelId);
                return;
            }

            _rawParcelQueue.Enqueue(parcel);
            _parcelSignal.Release();
            _logger.LogInformation("创建包裹成功并入原始队列 ParcelId={ParcelId}", parcelId);
        }

        /// <summary>
        /// 根据包裹编号计算包裹成熟时间。
        /// </summary>
        private static DateTime GetParcelMatureAt(long parcelId) {
            var createdAt = new DateTime(parcelId, DateTimeKind.Local);
            return createdAt + ParcelMatureDelay;
        }
    }
}
