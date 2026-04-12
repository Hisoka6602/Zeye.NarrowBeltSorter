using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.Sorting;
using Zeye.NarrowBeltSorter.Core.Events.Carrier;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;
using Zeye.NarrowBeltSorter.Core.Options.Carrier;

namespace Zeye.NarrowBeltSorter.Execution.Services.Carrier {

    /// <summary>
    /// 红外感应器小车管理器（内存实现）。
    /// </summary>
    public sealed class InfraredSensorCarrierManager : ICarrierManager {
        private readonly ILogger<InfraredSensorCarrierManager> _logger;
        private readonly SafeExecutor _safeExecutor;
        private readonly object _syncRoot = new();
        private readonly IOptionsMonitor<CarrierManagerOptions> _optionsMonitor;
        private readonly HashSet<long> _loadedCarrierIds = new();

        private IReadOnlyCollection<ICarrier> _carriers = [];
        /// <summary>
        /// 小车编号到实例的快速查找字典，与 _carriers 同步维护，提供 O(1) 查询。
        /// </summary>
        private Dictionary<long, ICarrier> _carrierMap = new();
        /// <summary>
        /// 按升序缓存的小车编号数组，与 _carriers 同步维护，避免 CurrentLoadingZoneCarrierId 每次重复分配。
        /// </summary>
        private long[] _sortedCarrierIds = [];
        private DropMode _dropMode = DropMode.Infrared;
        private bool _disposed;

        /// <summary>
        /// IsRingBuilt 的 volatile 字段，确保多线程可见性（写在 _syncRoot 内，读在锁外）。
        /// </summary>
        private volatile bool _isRingBuilt;

        /// <summary>
        /// 初始化红外感应器小车管理器，注入日志、安全执行器与配置监视器。
        /// </summary>
        public InfraredSensorCarrierManager(
            ILogger<InfraredSensorCarrierManager> logger,
            SafeExecutor safeExecutor,
            IOptionsMonitor<CarrierManagerOptions> optionsMonitor) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        }

        /// <inheritdoc />
        public IReadOnlyCollection<ICarrier> Carriers {
            get {
                lock (_syncRoot) {
                    return _carriers;
                }
            }
        }

        /// <inheritdoc />
        public bool IsRingBuilt => _isRingBuilt;

        /// <inheritdoc />
        public IReadOnlyDictionary<long, int> ChuteCarrierOffsetMap => _optionsMonitor.CurrentValue.ChuteCarrierOffsetMap;

        /// <inheritdoc />
        public int LoadingZoneCarrierOffset => _optionsMonitor.CurrentValue.LoadingZoneCarrierOffset;

        /// <inheritdoc />
        public DropMode DropMode {
            get {
                lock (_syncRoot) {
                    return _dropMode;
                }
            }
        }

        /// <inheritdoc />
        public long? CurrentInductionCarrierId { get; private set; }

        /// <inheritdoc />
        public IReadOnlyCollection<long> LoadedCarrierIds {
            get {
                lock (_syncRoot) {
                    return _loadedCarrierIds.ToArray();
                }
            }
        }

        /// <inheritdoc />
        public long? CurrentLoadingZoneCarrierId {
            get {
                lock (_syncRoot) {
                    if (!CurrentInductionCarrierId.HasValue || _sortedCarrierIds.Length == 0) {
                        return null;
                    }

                    var currentIndex = Array.IndexOf(_sortedCarrierIds, CurrentInductionCarrierId.Value);
                    if (currentIndex < 0) {
                        return null;
                    }

                    var loadingIndex = WrapIndex(currentIndex + _optionsMonitor.CurrentValue.LoadingZoneCarrierOffset, _sortedCarrierIds.Length);
                    return _sortedCarrierIds[loadingIndex];
                }
            }
        }

        /// <inheritdoc />
        public event EventHandler<CarrierRingBuiltEventArgs>? RingBuilt;

        /// <inheritdoc />
        public event EventHandler<CurrentInductionCarrierChangedEventArgs>? CurrentInductionCarrierChanged;

        /// <inheritdoc />
        public event EventHandler<LoadedCarrierEnteredChuteInductionEventArgs>? LoadedCarrierEnteredChuteInduction;

        /// <inheritdoc />
        public event EventHandler<CarrierLoadStatusChangedEventArgs>? CarrierLoadStatusChanged;

        /// <inheritdoc />
        public event EventHandler<CarrierApproachingTargetChuteEventArgs>? CarrierApproachingTargetChute;

        /// <inheritdoc />
        public event EventHandler<CarrierPassedForcedChuteEventArgs>? CarrierPassedForcedChute;

        /// <inheritdoc />
        public event EventHandler<CarrierConnectionStatusChangedEventArgs>? CarrierConnectionStatusChanged;

        /// <inheritdoc />
        public event EventHandler<CarrierManagerFaultedEventArgs>? Faulted;

        /// <summary>
        /// 建立连接（内存实现始终成功）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否成功。</returns>
        public ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        /// <summary>
        /// 断开连接（内存实现始终成功）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否成功。</returns>
        public ValueTask<bool> DisconnectAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        /// <summary>
        /// 按编号查找小车实例（O(1) 字典查询）。
        /// </summary>
        /// <param name="carrierId">小车编号。</param>
        /// <param name="carrier">输出小车实例。</param>
        /// <returns>是否找到。</returns>
        public bool TryGetCarrier(long carrierId, out ICarrier carrier) {
            lock (_syncRoot) {
                return _carrierMap.TryGetValue(carrierId, out carrier!);
            }
        }

        /// <summary>
        /// 设置落格模式。
        /// </summary>
        /// <param name="dropMode">目标落格模式。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否成功。</returns>
        public ValueTask<bool> SetDropModeAsync(DropMode dropMode, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_syncRoot) {
                ThrowIfDisposed();
                _dropMode = dropMode;
                return ValueTask.FromResult(true);
            }
        }

        /// <summary>
        /// 重建环形小车集合，并同步替换索引缓存。
        /// </summary>
        /// <param name="carrierIds">环形小车编号集合。</param>
        /// <param name="message">建环消息。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>建环成功返回 true。</returns>
        public ValueTask<bool> BuildRingAsync(
            IReadOnlyCollection<long> carrierIds,
            string? message = null,
            CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            if (carrierIds is null || carrierIds.Count == 0) {
                return ValueTask.FromResult(false);
            }

            CarrierRingBuiltEventArgs args;
            lock (_syncRoot) {
                ThrowIfDisposed();
                // 步骤1：构建有序的 ICarrier 列表，并同步维护 O(1) 查找字典和有序编号缓存。
                var sorted = carrierIds
                    .Distinct()
                    .OrderBy(x => x)
                    .Select(x => (ICarrier)new InfraredSensorCarrier(x, _safeExecutor))
                    .ToArray();
                foreach (var carrier in sorted) {
                    carrier.LoadStatusChanged += OnCarrierLoadStatusChanged;
                }

                // 步骤2： 替换集合前先释放旧小车订阅与资源，防止重建时残留事件引用。
                var sortedCarrierIds = sorted.Select(x => x.Id).ToArray();
                var carrierMap = sorted.ToDictionary(x => x.Id, x => x);
                ReleaseCarrierResources(_carriers);
                _loadedCarrierIds.Clear();
                if (CurrentInductionCarrierId.HasValue && !carrierMap.ContainsKey(CurrentInductionCarrierId.Value)) {
                    CurrentInductionCarrierId = null;
                }

                _carriers = sorted;
                _sortedCarrierIds = sortedCarrierIds;
                _carrierMap = carrierMap;
                _isRingBuilt = true;
                args = new CarrierRingBuiltEventArgs {
                    IsBuilt = true,
                    BuiltAt = DateTime.Now,
                    Message = message,
                };
            }

            _safeExecutor.PublishEventAsync(
                RingBuilt,
                this,
                args,
                "InfraredSensorCarrierManager.RingBuilt");

            _logger.LogInformation(
                "红外感应器小车建环完成 CarrierCount={CarrierCount} Message={Message}",
                _sortedCarrierIds.Length,
                message ?? string.Empty);

            return ValueTask.FromResult(true);
        }

        /// <summary>
        /// 更新当前感应位小车。
        /// </summary>
        /// <param name="carrierId">当前感应位小车编号。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否成功。</returns>
        public ValueTask<bool> UpdateCurrentInductionCarrierAsync(long? carrierId, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            CurrentInductionCarrierChangedEventArgs args;
            lock (_syncRoot) {
                ThrowIfDisposed();
                var oldCarrierId = CurrentInductionCarrierId;
                if (oldCarrierId == carrierId) {
                    return ValueTask.FromResult(true);
                }

                CurrentInductionCarrierId = carrierId;
                args = new CurrentInductionCarrierChangedEventArgs {
                    OldCarrierId = oldCarrierId,
                    NewCarrierId = carrierId,
                    ChangedAt = DateTime.Now,
                };
            }

            _safeExecutor.PublishEventAsync(
                CurrentInductionCarrierChanged,
                this,
                args,
                "InfraredSensorCarrierManager.CurrentInductionCarrierChanged");

            return ValueTask.FromResult(true);
        }

        /// <summary>
        /// 发布“小车靠近目标格口即将分拣”事件。
        /// </summary>
        /// <param name="args">事件载荷。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        public ValueTask PublishCarrierApproachingTargetChuteAsync(
            CarrierApproachingTargetChuteEventArgs args,
            CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            // 统一通过 SafeExecutor 非阻塞发布，方法返回即表示事件已成功投递到隔离执行器。
            _safeExecutor.PublishEventAsync(
                CarrierApproachingTargetChute,
                this,
                args,
                "InfraredSensorCarrierManager.CarrierApproachingTargetChute");
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// 发布“小车经过强排格口”事件。
        /// </summary>
        /// <param name="args">事件载荷。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        public ValueTask PublishCarrierPassedForcedChuteAsync(
            CarrierPassedForcedChuteEventArgs args,
            CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            // 统一通过 SafeExecutor 非阻塞发布，方法返回即表示事件已成功投递到隔离执行器。
            _safeExecutor.PublishEventAsync(
                CarrierPassedForcedChute,
                this,
                args,
                "InfraredSensorCarrierManager.CarrierPassedForcedChute");
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// 异步释放小车管理器资源。
        /// </summary>
        /// <returns>异步任务。</returns>
        public ValueTask DisposeAsync() {
            lock (_syncRoot) {
                if (_disposed) {
                    return ValueTask.CompletedTask;
                }

                ReleaseCarrierResources(_carriers);

                _carriers = [];
                _carrierMap = new Dictionary<long, ICarrier>();
                _sortedCarrierIds = [];
                _loadedCarrierIds.Clear();
                _disposed = true;
            }

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// 处理小车载货状态变化并转发管理器级事件。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="args">事件载荷。</param>
        private void OnCarrierLoadStatusChanged(object? sender, CarrierLoadStatusChangedEventArgs args) {
            CarrierLoadStatusChangedEventArgs managerArgs;
            lock (_syncRoot) {
                if (args.NewIsLoaded) {
                    _loadedCarrierIds.Add(args.CarrierId);
                }
                else {
                    _loadedCarrierIds.Remove(args.CarrierId);
                }

                managerArgs = args with {
                    CurrentInductionCarrierId = CurrentInductionCarrierId
                };
            }

            _safeExecutor.PublishEventAsync(
                CarrierLoadStatusChanged,
                this,
                managerArgs,
                "InfraredSensorCarrierManager.CarrierLoadStatusChanged");
        }

        /// <summary>
        /// 归一化环形索引。
        /// </summary>
        /// <param name="index">原始索引。</param>
        /// <param name="length">环形长度。</param>
        /// <returns>归一化后的索引。</returns>
        private static int WrapIndex(int index, int length) {
            if (length <= 0) {
                return 0;
            }

            var result = index % length;
            return result < 0 ? result + length : result;
        }

        /// <summary>
        /// 释放指定小车集合的事件订阅与对象资源。
        /// </summary>
        /// <param name="carriers">待释放小车集合。</param>
        private void ReleaseCarrierResources(IReadOnlyCollection<ICarrier> carriers) {
            foreach (var carrier in carriers) {
                carrier.LoadStatusChanged -= OnCarrierLoadStatusChanged;
                carrier.Dispose();
            }
        }

        /// <summary>
        /// 已释放状态守卫。
        /// </summary>
        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(InfraredSensorCarrierManager));
            }
        }
    }
}
