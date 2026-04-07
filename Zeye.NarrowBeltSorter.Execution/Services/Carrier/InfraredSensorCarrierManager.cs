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

        public InfraredSensorCarrierManager(
            ILogger<InfraredSensorCarrierManager> logger,
            SafeExecutor safeExecutor,
            IOptionsMonitor<CarrierManagerOptions> optionsMonitor) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        }

        public IReadOnlyCollection<ICarrier> Carriers {
            get {
                lock (_syncRoot) {
                    return _carriers;
                }
            }
        }

        public bool IsRingBuilt { get; private set; }

        public IReadOnlyDictionary<long, int> ChuteCarrierOffsetMap => _optionsMonitor.CurrentValue.ChuteCarrierOffsetMap;

        public int LoadingZoneCarrierOffset => _optionsMonitor.CurrentValue.LoadingZoneCarrierOffset;

        public DropMode DropMode {
            get {
                lock (_syncRoot) {
                    return _dropMode;
                }
            }
        }

        public long? CurrentInductionCarrierId { get; private set; }

        public IReadOnlyCollection<long> LoadedCarrierIds {
            get {
                lock (_syncRoot) {
                    return _loadedCarrierIds.ToArray();
                }
            }
        }

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

        public event EventHandler<CarrierRingBuiltEventArgs>? RingBuilt;

        public event EventHandler<CurrentInductionCarrierChangedEventArgs>? CurrentInductionCarrierChanged;

        public event EventHandler<LoadedCarrierEnteredChuteInductionEventArgs>? LoadedCarrierEnteredChuteInduction;

        public event EventHandler<LoadedCarrierPassedForcedChuteEventArgs>? LoadedCarrierPassedForcedChute;

        public event EventHandler<CarrierLoadStatusChangedEventArgs>? CarrierLoadStatusChanged;

        public event EventHandler<CarrierConnectionStatusChangedEventArgs>? CarrierConnectionStatusChanged;

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
                _carriers = sorted;
                _sortedCarrierIds = sorted.Select(x => x.Id).ToArray();
                _carrierMap = sorted.ToDictionary(x => x.Id, x => x);
                IsRingBuilt = true;
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
        /// 发布“载货小车进入目标格口感应区（靠近）”事件。
        /// </summary>
        /// <param name="args">事件载荷。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否成功。</returns>
        public ValueTask<bool> PublishLoadedCarrierEnteredChuteInductionAsync(
            LoadedCarrierEnteredChuteInductionEventArgs args,
            CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_syncRoot) {
                ThrowIfDisposed();
            }

            _safeExecutor.PublishEventAsync(
                LoadedCarrierEnteredChuteInduction,
                this,
                args,
                "InfraredSensorCarrierManager.LoadedCarrierEnteredChuteInduction");

            return ValueTask.FromResult(true);
        }

        /// <summary>
        /// 发布“载货小车经过强排格口”事件。
        /// </summary>
        /// <param name="args">事件载荷。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否成功。</returns>
        public ValueTask<bool> PublishLoadedCarrierPassedForcedChuteAsync(
            LoadedCarrierPassedForcedChuteEventArgs args,
            CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_syncRoot) {
                ThrowIfDisposed();
            }

            _safeExecutor.PublishEventAsync(
                LoadedCarrierPassedForcedChute,
                this,
                args,
                "InfraredSensorCarrierManager.LoadedCarrierPassedForcedChute");

            return ValueTask.FromResult(true);
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

                foreach (var carrier in _carriers) {
                    carrier.Dispose();
                }

                _carriers = [];
                _carrierMap = new Dictionary<long, ICarrier>();
                _sortedCarrierIds = [];
                _loadedCarrierIds.Clear();
                _disposed = true;
            }

            return ValueTask.CompletedTask;
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
        /// 已释放状态守卫。
        /// </summary>
        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(InfraredSensorCarrierManager));
            }
        }
    }
}
