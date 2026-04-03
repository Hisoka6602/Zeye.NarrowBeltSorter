using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.Io;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Sensor;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Models.Parcel;
using Zeye.NarrowBeltSorter.Core.Options.Sorting;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Execution.Services;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine {
    /// <summary>
    /// 分拣编排成熟起始来源链路测试。
    /// </summary>
    public sealed class SortingTaskOrchestrationMatureStartTests {
        /// <summary>
        /// 上车触发先到且在窗口内时，应消费该触发作为成熟起点。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenLoadingTriggerArrivesFirstWithinWindow_ShouldUseLoadingTriggerStart() {
            // 步骤1：构造测试依赖并启动服务。
            var fixedNow = DateTime.Now;
            var timing = new SortingTaskTimingOptions {
                ParcelMatureDelayMs = 60000,
                ParcelMatureStartSource = Core.Enums.Sorting.ParcelMatureStartSource.LoadingTriggerSensor,
                EnableFallbackToParcelCreateWhenLoadingTriggerMissing = false
            };
            var context = CreateContext(timing);
            await context.Service.StartAsync(CancellationToken.None);

            try {
                // 步骤2：先发上车触发源，再发创建包裹触发源。
                var loadingOccurredAt = fixedNow.AddMilliseconds(-1000);
                var createOccurredAt = fixedNow.AddMilliseconds(200);
                context.SensorManager.Publish(CreateSensorEvent(IoPointType.LoadingTriggerSensor, loadingOccurredAt));
                context.SensorManager.Publish(CreateSensorEvent(IoPointType.ParcelCreateSensor, createOccurredAt));

                // 步骤3：等待包裹创建后，直接校验成熟起始时间映射使用了上车触发源时间。
                var parcel = await WaitForSingleParcelAsync(context.ParcelManager);
                var mappedMatureStartAt = GetMappedMatureStartAtOrNull(context.Service, parcel.ParcelId);
                Assert.NotNull(mappedMatureStartAt);
                Assert.InRange((mappedMatureStartAt!.Value - loadingOccurredAt).TotalMilliseconds, -2000, 2000);
            }
            finally {
                await context.Service.StopAsync(CancellationToken.None);
            }
        }

        /// <summary>
        /// 仅创建包裹触发源时，应保持现有行为按创建时间计时。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenUsingParcelCreateSource_ShouldKeepCurrentBehavior() {
            // 步骤1：构造配置为创建包裹触发源并启动服务。
            var createOccurredAt = DateTime.Now;
            var timing = new SortingTaskTimingOptions {
                ParcelMatureDelayMs = 1,
                ParcelMatureStartSource = Core.Enums.Sorting.ParcelMatureStartSource.ParcelCreateSensor
            };
            var context = CreateContext(timing);
            await context.Service.StartAsync(CancellationToken.None);

            try {
                // 步骤2：仅发布创建包裹触发源事件。
                context.SensorManager.Publish(CreateSensorEvent(IoPointType.ParcelCreateSensor, createOccurredAt));
                var parcel = await WaitForSingleParcelAsync(context.ParcelManager);

                // 步骤3：断言包裹编号时间与创建触发时间一致（允许少量调度抖动）。
                var createdAtFromId = new DateTime(parcel.ParcelId, DateTimeKind.Local);
                Assert.InRange((createdAtFromId - createOccurredAt).TotalMilliseconds, -2000, 2000);
            }
            finally {
                await context.Service.StopAsync(CancellationToken.None);
            }
        }

        /// <summary>
        /// 选择上车触发源且缺失时，启用回退应按创建时间计时。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WhenLoadingTriggerMissingAndFallbackEnabled_ShouldFallbackToCreateTime() {
            // 步骤1：构造“上车触发缺失且允许回退”配置并启动服务。
            var createOccurredAt = DateTime.Now;
            var timing = new SortingTaskTimingOptions {
                ParcelMatureDelayMs = 1,
                ParcelMatureStartSource = Core.Enums.Sorting.ParcelMatureStartSource.LoadingTriggerSensor,
                EnableFallbackToParcelCreateWhenLoadingTriggerMissing = true
            };
            var context = CreateContext(timing);
            await context.Service.StartAsync(CancellationToken.None);

            try {
                // 步骤2：仅发布创建包裹触发源，模拟上车触发缺失。
                context.SensorManager.Publish(CreateSensorEvent(IoPointType.ParcelCreateSensor, createOccurredAt));
                var parcel = await WaitForSingleParcelAsync(context.ParcelManager);

                // 步骤3：断言成熟起始回退到创建触发时间。
                var matureStart = new DateTime(parcel.ParcelId, DateTimeKind.Local);
                Assert.InRange((matureStart - createOccurredAt).TotalMilliseconds, -2000, 2000);
            }
            finally {
                await context.Service.StopAsync(CancellationToken.None);
            }
        }

        /// <summary>
        /// 创建传感器状态变化事件。
        /// </summary>
        /// <param name="sensorType">传感器类型。</param>
        /// <param name="occurredAt">触发时间。</param>
        /// <returns>传感器状态变化事件。</returns>
        private static SensorStateChangedEventArgs CreateSensorEvent(IoPointType sensorType, DateTime occurredAt) {
            return new SensorStateChangedEventArgs(
                Point: 1,
                SensorName: sensorType.ToString(),
                SensorType: sensorType,
                OldState: IoState.Low,
                NewState: IoState.High,
                TriggerState: IoState.High,
                OccurredAtMs: occurredAt.Ticks / TimeSpan.TicksPerMillisecond);
        }

        /// <summary>
        /// 等待单个包裹创建完成。
        /// </summary>
        /// <param name="parcelManager">包裹管理器。</param>
        /// <returns>创建的包裹。</returns>
        private static async Task<ParcelInfo> WaitForSingleParcelAsync(RecordingParcelManager parcelManager) {
            var timeoutAt = DateTime.Now.AddSeconds(3);
            while (DateTime.Now < timeoutAt) {
                var parcel = parcelManager.GetFirstCreatedParcelOrNull();
                if (parcel is not null) {
                    return parcel;
                }

                await Task.Delay(20);
            }

            throw new TimeoutException("等待包裹创建超时。");
        }

        /// <summary>
        /// 构造分拣编排测试上下文。
        /// </summary>
        /// <param name="timingOptions">时序配置。</param>
        /// <returns>测试上下文。</returns>
        private static OrchestrationContext CreateContext(SortingTaskTimingOptions timingOptions) {
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var sensorManager = new RecordingSensorManager();
            var systemStateManager = new FixedSystemStateManager();
            var parcelManager = new RecordingParcelManager();
            var carrierManager = new StubCarrierManager();
            var carrierLoadingService = new SortingTaskCarrierLoadingService(
                NullLogger<SortingTaskCarrierLoadingService>.Instance,
                carrierManager,
                parcelManager);
            var dropService = new SortingTaskDropOrchestrationService(
                NullLogger<SortingTaskDropOrchestrationService>.Instance,
                carrierManager,
                parcelManager,
                new StubChuteManager(),
                carrierLoadingService,
                OptionsMonitorTestHelper.Create(new SortingTaskTimingOptions()));
            var service = new SortingTaskOrchestrationService(
                NullLogger<SortingTaskOrchestrationService>.Instance,
                safeExecutor,
                parcelManager,
                systemStateManager,
                sensorManager,
                carrierManager,
                carrierLoadingService,
                dropService,
                OptionsMonitorTestHelper.Create(timingOptions));
            return new OrchestrationContext(
                Service: service,
                SensorManager: sensorManager,
                ParcelManager: parcelManager,
                Orchestration: carrierLoadingService);
        }

        /// <summary>
        /// 分拣编排测试上下文。
        /// </summary>
        /// <param name="Service">服务实例。</param>
        /// <param name="SensorManager">传感器管理器。</param>
        /// <param name="ParcelManager">包裹管理器。</param>
        /// <param name="Orchestration">上车编排服务。</param>
        private readonly record struct OrchestrationContext(
            SortingTaskOrchestrationService Service,
            RecordingSensorManager SensorManager,
            RecordingParcelManager ParcelManager,
            SortingTaskCarrierLoadingService Orchestration);

        /// <summary>
        /// 分拣编排服务测试可见性扩展。
        /// </summary>
        /// <summary>
        /// 读取包裹成熟起始时间映射值。
        /// </summary>
        /// <param name="service">分拣编排服务。</param>
        /// <param name="parcelId">包裹编号。</param>
        /// <returns>映射值；不存在时返回 null。</returns>
        private static DateTime? GetMappedMatureStartAtOrNull(SortingTaskOrchestrationService service, long parcelId) {
            var field = typeof(SortingTaskOrchestrationService)
                .GetField("_parcelMatureStartAtMap", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var map = field?.GetValue(service) as IDictionary<long, DateTime>;
            if (map is not null && map.TryGetValue(parcelId, out var value)) {
                return value;
            }

            return null;
        }

        /// <summary>
        /// 固定运行态系统状态管理器桩。
        /// </summary>
        private sealed class FixedSystemStateManager : ISystemStateManager {
            /// <inheritdoc />
            public SystemState CurrentState => SystemState.Running;

            /// <inheritdoc />
            public event EventHandler<Core.Events.System.StateChangeEventArgs>? StateChanged;

            /// <inheritdoc />
            public Task<bool> ChangeStateAsync(SystemState targetState, CancellationToken cancellationToken = default) {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(false);
            }

            /// <inheritdoc />
            public void Dispose() {
            }
        }

        /// <summary>
        /// 传感器管理器测试桩。
        /// </summary>
        private sealed class RecordingSensorManager : ISensorManager {
            /// <inheritdoc />
            public Core.Enums.System.SensorMonitoringStatus Status => Core.Enums.System.SensorMonitoringStatus.Monitoring;

            /// <inheritdoc />
            public bool IsMonitoring => true;

            /// <inheritdoc />
            public IReadOnlyList<Core.Models.Sensor.SensorInfo> Sensors => [];

            /// <inheritdoc />
            public event EventHandler<SensorStateChangedEventArgs>? SensorStateChanged;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Io.SensorMonitoringStatusChangedEventArgs>? MonitoringStatusChanged;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Io.SensorFaultedEventArgs>? Faulted;

            /// <summary>
            /// 发布传感器事件。
            /// </summary>
            /// <param name="args">事件参数。</param>
            public void Publish(SensorStateChangedEventArgs args) {
                SensorStateChanged?.Invoke(this, args);
            }

            /// <inheritdoc />
            public ValueTask StartMonitoringAsync(CancellationToken cancellationToken = default) {
                return ValueTask.CompletedTask;
            }

            /// <inheritdoc />
            public ValueTask StopMonitoringAsync(CancellationToken cancellationToken = default) {
                return ValueTask.CompletedTask;
            }
        }

        /// <summary>
        /// 记录创建包裹的包裹管理器桩。
        /// </summary>
        private sealed class RecordingParcelManager : IParcelManager {
            private readonly List<ParcelInfo> _createdParcels = [];

            /// <inheritdoc />
            public IReadOnlyCollection<ParcelInfo> Parcels => _createdParcels;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Parcel.ParcelCreatedEventArgs>? ParcelCreated;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Parcel.ParcelTargetChuteUpdatedEventArgs>? ParcelTargetChuteUpdated;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Parcel.ParcelCarriersUpdatedEventArgs>? ParcelCarriersUpdated;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Parcel.ParcelDroppedEventArgs>? ParcelDropped;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Parcel.ParcelRemovedEventArgs>? ParcelRemoved;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Parcel.ParcelManagerFaultedEventArgs>? Faulted;

            /// <summary>
            /// 获取首个已创建包裹。
            /// </summary>
            /// <returns>包裹或 null。</returns>
            public ParcelInfo? GetFirstCreatedParcelOrNull() {
                lock (_createdParcels) {
                    return _createdParcels.Count > 0 ? _createdParcels[0] : null;
                }
            }

            /// <inheritdoc />
            public ValueTask<bool> CreateAsync(ParcelInfo parcel, CancellationToken cancellationToken = default) {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_createdParcels) {
                    _createdParcels.Add(parcel);
                }

                ParcelCreated?.Invoke(this, new Core.Events.Parcel.ParcelCreatedEventArgs {
                    ParcelId = parcel.ParcelId,
                    Parcel = parcel,
                    CreatedAt = DateTime.Now
                });
                return ValueTask.FromResult(true);
            }

            /// <inheritdoc />
            public ValueTask<bool> AssignTargetChuteAsync(long parcelId, long targetChuteId, DateTime assignedAt, CancellationToken cancellationToken = default) {
                return ValueTask.FromResult(true);
            }

            /// <inheritdoc />
            public ValueTask<bool> BindCarrierAsync(long parcelId, long carrierId, DateTime updatedAt, CancellationToken cancellationToken = default) {
                return ValueTask.FromResult(true);
            }

            /// <inheritdoc />
            public ValueTask<bool> UnbindCarrierAsync(long parcelId, long carrierId, DateTime updatedAt, CancellationToken cancellationToken = default) {
                return ValueTask.FromResult(true);
            }

            /// <inheritdoc />
            public ValueTask<bool> ReplaceCarriersAsync(long parcelId, IReadOnlyList<long> carrierIds, DateTime updatedAt, CancellationToken cancellationToken = default) {
                return ValueTask.FromResult(true);
            }

            /// <inheritdoc />
            public ValueTask<bool> ClearCarriersAsync(long parcelId, DateTime updatedAt, CancellationToken cancellationToken = default) {
                return ValueTask.FromResult(true);
            }

            /// <inheritdoc />
            public ValueTask<bool> MarkDroppedAsync(long parcelId, long actualChuteId, DateTime droppedAt, CancellationToken cancellationToken = default) {
                return ValueTask.FromResult(true);
            }

            /// <inheritdoc />
            public ValueTask<bool> RemoveAsync(long parcelId, string? reason = null, CancellationToken cancellationToken = default) {
                return ValueTask.FromResult(true);
            }

            /// <inheritdoc />
            public ValueTask ClearAsync(string? reason = null, CancellationToken cancellationToken = default) {
                lock (_createdParcels) {
                    _createdParcels.Clear();
                }
                return ValueTask.CompletedTask;
            }

            /// <inheritdoc />
            public bool TryGet(long parcelId, out ParcelInfo parcel) {
                lock (_createdParcels) {
                    parcel = _createdParcels.FirstOrDefault(x => x.ParcelId == parcelId)!;
                    return parcel is not null;
                }
            }

            /// <inheritdoc />
            public void Dispose() {
            }
        }

        /// <summary>
        /// 小车管理器最小桩。
        /// </summary>
        private sealed class StubCarrierManager : ICarrierManager {
            /// <inheritdoc />
            public IReadOnlyCollection<ICarrier> Carriers => [];

            /// <inheritdoc />
            public bool IsRingBuilt => false;

            /// <inheritdoc />
            public IReadOnlyDictionary<long, int> ChuteCarrierOffsetMap => new Dictionary<long, int>();

            /// <inheritdoc />
            public int LoadingZoneCarrierOffset => 0;

            /// <inheritdoc />
            public Core.Enums.Sorting.DropMode DropMode => Core.Enums.Sorting.DropMode.Infrared;

            /// <inheritdoc />
            public long? CurrentInductionCarrierId => null;

            /// <inheritdoc />
            public IReadOnlyCollection<long> LoadedCarrierIds => [];

            /// <inheritdoc />
            public long? CurrentLoadingZoneCarrierId => null;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Carrier.CarrierRingBuiltEventArgs>? RingBuilt;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Carrier.CurrentInductionCarrierChangedEventArgs>? CurrentInductionCarrierChanged;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Carrier.LoadedCarrierEnteredChuteInductionEventArgs>? LoadedCarrierEnteredChuteInduction;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Carrier.CarrierLoadStatusChangedEventArgs>? CarrierLoadStatusChanged;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Carrier.CarrierConnectionStatusChangedEventArgs>? CarrierConnectionStatusChanged;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Carrier.CarrierManagerFaultedEventArgs>? Faulted;

            /// <inheritdoc />
            public ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

            /// <inheritdoc />
            public ValueTask<bool> DisconnectAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

            /// <inheritdoc />
            public bool TryGetCarrier(long carrierId, out ICarrier carrier) {
                carrier = default!;
                return false;
            }

            /// <inheritdoc />
            public ValueTask<bool> SetDropModeAsync(Core.Enums.Sorting.DropMode dropMode, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

            /// <inheritdoc />
            public ValueTask<bool> BuildRingAsync(IReadOnlyCollection<long> carrierIds, string? message = null, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

            /// <inheritdoc />
            public ValueTask<bool> UpdateCurrentInductionCarrierAsync(long? carrierId, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

            /// <inheritdoc />
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        /// <summary>
        /// 格口管理器最小桩。
        /// </summary>
        private sealed class StubChuteManager : IChuteManager {
            /// <inheritdoc />
            public IReadOnlyCollection<IChute> Chutes => [];

            /// <inheritdoc />
            public long? ForcedChuteId => null;

            /// <inheritdoc />
            public IReadOnlySet<long> TargetChuteIds => new HashSet<long>();

            /// <inheritdoc />
            public IReadOnlyDictionary<long, string> ChuteConfigurationSnapshot => new Dictionary<long, string>();

            /// <inheritdoc />
            public IReadOnlySet<long> LockedChuteIds => new HashSet<long>();

            /// <inheritdoc />
            public Core.Enums.Device.DeviceConnectionStatus ConnectionStatus => Core.Enums.Device.DeviceConnectionStatus.Disconnected;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Chutes.ChuteParcelDroppedEventArgs>? ParcelDropped;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Chutes.ForcedChuteChangedEventArgs>? ForcedChuteChanged;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Chutes.ChuteConfigurationChangedEventArgs>? ChuteConfigurationChanged;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Chutes.ChuteLockStatusChangedEventArgs>? ChuteLockStatusChanged;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Chutes.ChuteManagerConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

            /// <inheritdoc />
            public event EventHandler<Core.Events.Chutes.ChuteManagerFaultedEventArgs>? Faulted;

            /// <inheritdoc />
            public ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

            /// <inheritdoc />
            public ValueTask<bool> DisconnectAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

            /// <inheritdoc />
            public ValueTask<bool> SetForcedChuteAsync(long? chuteId, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

            /// <inheritdoc />
            public ValueTask<bool> AddTargetChuteAsync(long chuteId, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

            /// <inheritdoc />
            public ValueTask<bool> RemoveTargetChuteAsync(long chuteId, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

            /// <inheritdoc />
            public ValueTask<bool> SetChuteLockedAsync(long chuteId, bool isLocked, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

            /// <inheritdoc />
            public bool TryGetChute(long chuteId, out IChute chute) {
                chute = default!;
                return false;
            }

            /// <inheritdoc />
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
