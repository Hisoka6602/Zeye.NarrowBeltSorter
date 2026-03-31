using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.Parcel;
using Zeye.NarrowBeltSorter.Core.Events.Parcel;
using Zeye.NarrowBeltSorter.Core.Models.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;

namespace Zeye.NarrowBeltSorter.Execution.Parcel {

    public sealed class ParcelManager : IParcelManager {
        private readonly ILogger<ParcelManager> _logger;
        private readonly SafeExecutor _safeExecutor;
        private readonly ConcurrentDictionary<long, ParcelInfo> _parcels;
        private readonly object[] _gates;
        private readonly int _gateMask;
        private int _isClearing;

        public ParcelManager(
            ILogger<ParcelManager> logger,
            SafeExecutor safeExecutor,
            int initialCapacity = 4096,
            int gateCountPowerOfTwo = 1024) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));

            if (gateCountPowerOfTwo <= 0 || (gateCountPowerOfTwo & (gateCountPowerOfTwo - 1)) != 0) {
                throw new ArgumentOutOfRangeException(nameof(gateCountPowerOfTwo), "参数无效：gateCountPowerOfTwo 必须为 2 的幂。");
            }

            _parcels = new ConcurrentDictionary<long, ParcelInfo>(Environment.ProcessorCount, initialCapacity);
            _gates = new object[gateCountPowerOfTwo];
            for (var i = 0; i < _gates.Length; i++) {
                _gates[i] = new object();
            }

            _gateMask = gateCountPowerOfTwo - 1;
            Parcels = new ParcelInfoReadOnlyView(_parcels);
        }

        public IReadOnlyCollection<ParcelInfo> Parcels { get; }

        public event EventHandler<ParcelCreatedEventArgs>? ParcelCreated;

        public event EventHandler<ParcelTargetChuteUpdatedEventArgs>? ParcelTargetChuteUpdated;

        public event EventHandler<ParcelCarriersUpdatedEventArgs>? ParcelCarriersUpdated;

        public event EventHandler<ParcelDroppedEventArgs>? ParcelDropped;

        public event EventHandler<ParcelRemovedEventArgs>? ParcelRemoved;

        public event EventHandler<ParcelManagerFaultedEventArgs>? Faulted;

        public ValueTask<bool> CreateAsync(ParcelInfo parcel, CancellationToken cancellationToken = default) {
            if (parcel is null || IsRejected("CreateAsync", cancellationToken, parcel.ParcelId)) {
                return ValueTask.FromResult(false);
            }

            return ExecuteMutation("CreateAsync", parcel.ParcelId, () => {
                var gate = GetGate(parcel.ParcelId);
                var added = false;

                lock (gate) {
                    if (parcel.ParcelId <= 0) {
                        throw new ArgumentOutOfRangeException(nameof(parcel.ParcelId), "参数无效：ParcelId 必须为正数。");
                    }

                    added = _parcels.TryAdd(parcel.ParcelId, parcel);
                }

                if (!added) {
                    return false;
                }

                var createdAt = DateTime.Now;
                RaiseSafe(ParcelCreated, new ParcelCreatedEventArgs {
                    ParcelId = parcel.ParcelId,
                    Parcel = parcel,
                    CreatedAt = createdAt,
                });

                ParcelManagerLog.Created(_logger, parcel.ParcelId, parcel.BarCode, createdAt);
                return true;
            });
        }

        public ValueTask<bool> AssignTargetChuteAsync(long parcelId, long targetChuteId, DateTime assignedAt, CancellationToken cancellationToken = default) {
            if (IsRejected("AssignTargetChuteAsync", cancellationToken, parcelId) || targetChuteId <= 0) {
                return ValueTask.FromResult(false);
            }

            return ExecuteMutation("AssignTargetChuteAsync", parcelId, () => {
                var gate = GetGate(parcelId);
                ParcelTargetChuteUpdatedEventArgs args;
                long oldTargetChuteIdForLog;

                lock (gate) {
                    if (!_parcels.TryGetValue(parcelId, out var parcel)) {
                        return false;
                    }

                    var snapshot = parcel.GetTargetChuteSnapshot();
                    oldTargetChuteIdForLog = snapshot.TargetChuteId > 0 ? snapshot.TargetChuteId : 0;
                    parcel.SetTargetChute(targetChuteId, NormalizeLocalTime(assignedAt));

                    args = new ParcelTargetChuteUpdatedEventArgs {
                        ParcelId = parcelId,
                        OldTargetChuteId = snapshot.TargetChuteId > 0 ? snapshot.TargetChuteId : null,
                        NewTargetChuteId = targetChuteId,
                        AssignedAt = NormalizeLocalTime(assignedAt),
                    };
                }

                RaiseSafe(ParcelTargetChuteUpdated, args);
                ParcelManagerLog.TargetUpdated(_logger, parcelId, oldTargetChuteIdForLog, targetChuteId, args.AssignedAt);
                return true;
            });
        }

        public ValueTask<bool> BindCarrierAsync(long parcelId, long carrierId, DateTime updatedAt, CancellationToken cancellationToken = default) {
            if (IsRejected("BindCarrierAsync", cancellationToken, parcelId) || carrierId <= 0) {
                return ValueTask.FromResult(false);
            }

            return UpdateCarriersAsync(parcelId, carrierId, updatedAt, ParcelCarriersChangeType.Bound, (parcel, id) => {
                parcel.BindCarrier(id);
            });
        }

        public ValueTask<bool> UnbindCarrierAsync(long parcelId, long carrierId, DateTime updatedAt, CancellationToken cancellationToken = default) {
            if (IsRejected("UnbindCarrierAsync", cancellationToken, parcelId) || carrierId <= 0) {
                return ValueTask.FromResult(false);
            }

            return UpdateCarriersAsync(parcelId, carrierId, updatedAt, ParcelCarriersChangeType.Unbound, (parcel, id) => {
                parcel.UnbindCarrier(id);
            });
        }

        public ValueTask<bool> ReplaceCarriersAsync(long parcelId, IReadOnlyList<long> carrierIds, DateTime updatedAt, CancellationToken cancellationToken = default) {
            if (IsRejected("ReplaceCarriersAsync", cancellationToken, parcelId) || carrierIds is null) {
                return ValueTask.FromResult(false);
            }

            return ExecuteMutation("ReplaceCarriersAsync", parcelId, () => {
                var gate = GetGate(parcelId);
                ParcelCarriersUpdatedEventArgs args;

                lock (gate) {
                    if (!_parcels.TryGetValue(parcelId, out var parcel)) {
                        return false;
                    }

                    parcel.ClearCarriers();
                    foreach (var carrierId in carrierIds) {
                        if (carrierId <= 0) {
                            throw new ArgumentOutOfRangeException(nameof(carrierIds), "参数无效：carrierIds 仅允许正数小车Id。");
                        }

                        parcel.BindCarrier(carrierId);
                    }

                    args = new ParcelCarriersUpdatedEventArgs {
                        ParcelId = parcelId,
                        ChangeType = ParcelCarriersChangeType.Replaced,
                        CarrierId = null,
                        UpdatedAt = NormalizeLocalTime(updatedAt),
                        CarrierIdsSnapshot = parcel.CarrierIds.ToArray(),
                    };
                }

                RaiseSafe(ParcelCarriersUpdated, args);
                ParcelManagerLog.CarriersUpdated(_logger, parcelId, ParcelCarriersChangeType.Replaced, null, args.UpdatedAt, args.CarrierIdsSnapshot.Count);
                return true;
            });
        }

        public ValueTask<bool> ClearCarriersAsync(long parcelId, DateTime updatedAt, CancellationToken cancellationToken = default) {
            if (IsRejected("ClearCarriersAsync", cancellationToken, parcelId)) {
                return ValueTask.FromResult(false);
            }

            return ExecuteMutation("ClearCarriersAsync", parcelId, () => {
                var gate = GetGate(parcelId);
                ParcelCarriersUpdatedEventArgs args;

                lock (gate) {
                    if (!_parcels.TryGetValue(parcelId, out var parcel)) {
                        return false;
                    }

                    parcel.ClearCarriers();
                    args = new ParcelCarriersUpdatedEventArgs {
                        ParcelId = parcelId,
                        ChangeType = ParcelCarriersChangeType.Cleared,
                        CarrierId = null,
                        UpdatedAt = NormalizeLocalTime(updatedAt),
                        CarrierIdsSnapshot = [],
                    };
                }

                RaiseSafe(ParcelCarriersUpdated, args);
                ParcelManagerLog.CarriersUpdated(_logger, parcelId, ParcelCarriersChangeType.Cleared, null, args.UpdatedAt, 0);
                return true;
            });
        }

        public ValueTask<bool> MarkDroppedAsync(long parcelId, long actualChuteId, DateTime droppedAt, CancellationToken cancellationToken = default) {
            if (IsRejected("MarkDroppedAsync", cancellationToken, parcelId) || actualChuteId <= 0) {
                return ValueTask.FromResult(false);
            }

            return ExecuteMutation("MarkDroppedAsync", parcelId, () => {
                var gate = GetGate(parcelId);
                ParcelDroppedEventArgs args;

                lock (gate) {
                    if (!_parcels.TryGetValue(parcelId, out var parcel)) {
                        return false;
                    }

                    var localDroppedAt = NormalizeLocalTime(droppedAt);
                    parcel.MarkDropped(actualChuteId, localDroppedAt);
                    args = new ParcelDroppedEventArgs {
                        ParcelId = parcelId,
                        ActualChuteId = actualChuteId,
                        DroppedAt = localDroppedAt,
                    };
                }

                RaiseSafe(ParcelDropped, args);
                ParcelManagerLog.Dropped(_logger, parcelId, actualChuteId, args.DroppedAt);
                return true;
            });
        }

        public ValueTask<bool> RemoveAsync(long parcelId, string? reason = null, CancellationToken cancellationToken = default) {
            if (IsRejected("RemoveAsync", cancellationToken, parcelId)) {
                return ValueTask.FromResult(false);
            }

            return ExecuteMutation("RemoveAsync", parcelId, () => {
                var gate = GetGate(parcelId);
                var removed = false;

                lock (gate) {
                    removed = _parcels.TryRemove(parcelId, out _);
                }

                if (!removed) {
                    return false;
                }

                var removedAt = DateTime.Now;
                RaiseSafe(ParcelRemoved, new ParcelRemovedEventArgs {
                    ParcelId = parcelId,
                    Reason = reason,
                    RemovedAt = removedAt,
                });

                ParcelManagerLog.Removed(_logger, parcelId, reason, removedAt);
                return true;
            });
        }

        public ValueTask ClearAsync(string? reason = null, CancellationToken cancellationToken = default) {
            if (cancellationToken.IsCancellationRequested) {
                return ValueTask.CompletedTask;
            }

            if (Interlocked.Exchange(ref _isClearing, 1) == 1) {
                return ValueTask.CompletedTask;
            }

            try {
                var countBefore = _parcels.Count;
                _parcels.Clear();
                ParcelManagerLog.Cleared(_logger, reason, countBefore, DateTime.Now);
            }
            finally {
                Volatile.Write(ref _isClearing, 0);
            }

            return ValueTask.CompletedTask;
        }

        public bool TryGet(long parcelId, out ParcelInfo parcel) {
            return _parcels.TryGetValue(parcelId, out parcel!);
        }

        public void Dispose() {
            _parcels.Clear();
            GC.SuppressFinalize(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private object GetGate(long parcelId) {
            var h = (uint)parcelId ^ (uint)(parcelId >> 32);
            var idx = (int)(h & (uint)_gateMask);
            return _gates[idx];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static DateTime NormalizeLocalTime(DateTime value) {
            if (value == default) {
                return DateTime.Now;
            }

            return value.Kind switch {
                DateTimeKind.Local => value,
                DateTimeKind.Utc => value.ToLocalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Local),
            };
        }

        private bool IsRejected(string operation, CancellationToken cancellationToken, long parcelId) {
            if (Volatile.Read(ref _isClearing) == 1 || cancellationToken.IsCancellationRequested || parcelId <= 0) {
                ParcelManagerLog.Rejected(_logger, operation, parcelId);
                return true;
            }

            return false;
        }

        private ValueTask<bool> UpdateCarriersAsync(
            long parcelId,
            long carrierId,
            DateTime updatedAt,
            ParcelCarriersChangeType changeType,
            Action<ParcelInfo, long> updater) {
            return ExecuteMutation(changeType.ToString(), parcelId, () => {
                var gate = GetGate(parcelId);
                ParcelCarriersUpdatedEventArgs args;

                lock (gate) {
                    if (!_parcels.TryGetValue(parcelId, out var parcel)) {
                        return false;
                    }

                    updater(parcel, carrierId);
                    args = new ParcelCarriersUpdatedEventArgs {
                        ParcelId = parcelId,
                        ChangeType = changeType,
                        CarrierId = carrierId,
                        UpdatedAt = NormalizeLocalTime(updatedAt),
                        CarrierIdsSnapshot = parcel.CarrierIds.ToArray(),
                    };
                }

                RaiseSafe(ParcelCarriersUpdated, args);
                ParcelManagerLog.CarriersUpdated(_logger, parcelId, changeType, carrierId, args.UpdatedAt, args.CarrierIdsSnapshot.Count);
                return true;
            });
        }

        private ValueTask<bool> ExecuteMutation(string operation, long parcelId, Func<bool> mutation) {
            try {
                var ok = mutation();
                return ValueTask.FromResult(ok);
            }
            catch (Exception ex) {
                RaiseFaulted(operation, parcelId, ex);
                return ValueTask.FromResult(false);
            }
        }

        private void RaiseFaulted(string operation, long? parcelId, Exception exception) {
            var message = parcelId.HasValue
                ? $"包裹管理器发生异常：Operation={operation} ParcelId={parcelId.Value}"
                : $"包裹管理器发生异常：Operation={operation}";

            ParcelManagerLog.Faulted(_logger, message, exception);
            RaiseSafe(Faulted, new ParcelManagerFaultedEventArgs {
                Message = message,
                Exception = exception,
                OccurredAt = DateTime.Now,
            });
        }

        private void RaiseSafe<TArgs>(EventHandler<TArgs>? handler, TArgs args) {
            if (handler is null) {
                return;
            }

            _safeExecutor.PublishEventAsync(handler, this, args, "ParcelManager.EventDispatch");
        }
    }
}
