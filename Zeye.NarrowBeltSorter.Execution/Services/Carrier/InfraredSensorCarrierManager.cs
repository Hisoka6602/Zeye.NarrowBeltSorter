using System;
using System.Linq;
using System.Text;
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
        private readonly IReadOnlyDictionary<long, int> _chuteCarrierOffsetMap;
        private readonly int _loadingZoneCarrierOffset;
        private readonly HashSet<long> _loadedCarrierIds = new();

        private IReadOnlyCollection<ICarrier> _carriers = [];
        private DropMode _dropMode = DropMode.Infrared;
        private bool _disposed;

        public InfraredSensorCarrierManager(
            ILogger<InfraredSensorCarrierManager> logger,
            SafeExecutor safeExecutor,
            IOptions<CarrierManagerOptions> options) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            var value = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _loadingZoneCarrierOffset = value.LoadingZoneCarrierOffset;
            _chuteCarrierOffsetMap = new Dictionary<long, int>(value.ChuteCarrierOffsetMap);
        }

        public IReadOnlyCollection<ICarrier> Carriers {
            get {
                lock (_syncRoot) {
                    return _carriers;
                }
            }
        }

        public bool IsRingBuilt { get; private set; }

        public IReadOnlyDictionary<long, int> ChuteCarrierOffsetMap => _chuteCarrierOffsetMap;

        public int LoadingZoneCarrierOffset => _loadingZoneCarrierOffset;

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
                    if (!CurrentInductionCarrierId.HasValue || _carriers.Count == 0) {
                        return null;
                    }

                    var carrierIds = _carriers.Select(x => x.Id).OrderBy(x => x).ToArray();
                    var currentIndex = Array.IndexOf(carrierIds, CurrentInductionCarrierId.Value);
                    if (currentIndex < 0) {
                        return null;
                    }

                    var loadingIndex = WrapIndex(currentIndex + _loadingZoneCarrierOffset, carrierIds.Length);
                    return carrierIds[loadingIndex];
                }
            }
        }

        public event EventHandler<CarrierRingBuiltEventArgs>? RingBuilt;

        public event EventHandler<CurrentInductionCarrierChangedEventArgs>? CurrentInductionCarrierChanged;

        public event EventHandler<LoadedCarrierEnteredChuteInductionEventArgs>? LoadedCarrierEnteredChuteInduction;

        public event EventHandler<CarrierLoadStatusChangedEventArgs>? CarrierLoadStatusChanged;

        public event EventHandler<CarrierConnectionStatusChangedEventArgs>? CarrierConnectionStatusChanged;

        public event EventHandler<CarrierManagerFaultedEventArgs>? Faulted;

        public ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> DisconnectAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        public bool TryGetCarrier(long carrierId, out ICarrier carrier) {
            lock (_syncRoot) {
                carrier = _carriers.FirstOrDefault(x => x.Id == carrierId)!;
                return carrier is not null;
            }
        }

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
                _carriers = carrierIds
                    .Distinct()
                    .OrderBy(x => x)
                    .Select(x => (ICarrier)new InfraredSensorCarrier(x))
                    .ToArray();
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
                carrierIds.Count,
                message ?? string.Empty);

            return ValueTask.FromResult(true);
        }

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

        public ValueTask DisposeAsync() {
            lock (_syncRoot) {
                if (_disposed) {
                    return ValueTask.CompletedTask;
                }

                foreach (var carrier in _carriers) {
                    carrier.Dispose();
                }

                _carriers = [];
                _loadedCarrierIds.Clear();
                _disposed = true;
            }

            return ValueTask.CompletedTask;
        }

        private static int WrapIndex(int index, int length) {
            if (length <= 0) {
                return 0;
            }

            var result = index % length;
            return result < 0 ? result + length : result;
        }

        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(InfraredSensorCarrierManager));
            }
        }
    }
}
