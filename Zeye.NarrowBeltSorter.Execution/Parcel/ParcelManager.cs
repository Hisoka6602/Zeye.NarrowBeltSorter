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

        /// <summary>
        /// 创建包裹并触发 <see cref="ParcelCreated"/> 事件。
        /// </summary>
        /// <param name="parcel">包裹信息，<see cref="ParcelInfo.ParcelId"/> 须为正数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>创建成功返回 true；包裹已存在或参数不合法返回 false。</returns>
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

        /// <summary>
        /// 为指定包裹分配目标格口，并触发 <see cref="ParcelTargetChuteUpdated"/> 事件。
        /// </summary>
        /// <param name="parcelId">包裹 Id。</param>
        /// <param name="targetChuteId">目标格口 Id，须大于 0。</param>
        /// <param name="assignedAt">分配时刻（本地时间）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>更新成功返回 true；包裹不存在或参数不合法返回 false。</returns>
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

        /// <summary>
        /// 将指定小车绑定到包裹，并触发 <see cref="ParcelCarriersUpdated"/> 事件。
        /// </summary>
        /// <param name="parcelId">包裹 Id。</param>
        /// <param name="carrierId">小车 Id，须大于 0。</param>
        /// <param name="updatedAt">绑定时刻（本地时间）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>绑定成功返回 true；包裹不存在或参数不合法返回 false。</returns>
        public ValueTask<bool> BindCarrierAsync(long parcelId, long carrierId, DateTime updatedAt, CancellationToken cancellationToken = default) {
            if (IsRejected("BindCarrierAsync", cancellationToken, parcelId) || carrierId <= 0) {
                return ValueTask.FromResult(false);
            }

            return UpdateCarriersAsync(parcelId, carrierId, updatedAt, ParcelCarriersChangeType.Bound, (parcel, id) => {
                parcel.BindCarrier(id);
            });
        }

        /// <summary>
        /// 从包裹解绑指定小车，并触发 <see cref="ParcelCarriersUpdated"/> 事件。
        /// </summary>
        /// <param name="parcelId">包裹 Id。</param>
        /// <param name="carrierId">小车 Id，须大于 0。</param>
        /// <param name="updatedAt">解绑时刻（本地时间）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>解绑成功返回 true；包裹不存在或参数不合法返回 false。</returns>
        public ValueTask<bool> UnbindCarrierAsync(long parcelId, long carrierId, DateTime updatedAt, CancellationToken cancellationToken = default) {
            if (IsRejected("UnbindCarrierAsync", cancellationToken, parcelId) || carrierId <= 0) {
                return ValueTask.FromResult(false);
            }

            return UpdateCarriersAsync(parcelId, carrierId, updatedAt, ParcelCarriersChangeType.Unbound, (parcel, id) => {
                parcel.UnbindCarrier(id);
            });
        }

        /// <summary>
        /// 清空包裹当前小车列表并用新列表替换，触发 <see cref="ParcelCarriersUpdated"/> 事件。
        /// </summary>
        /// <param name="parcelId">包裹 Id。</param>
        /// <param name="carrierIds">新小车 Id 列表，每项须大于 0。</param>
        /// <param name="updatedAt">更新时刻（本地时间）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>替换成功返回 true；包裹不存在或参数不合法返回 false。</returns>
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

        /// <summary>
        /// 清空包裹的小车列表，并触发 <see cref="ParcelCarriersUpdated"/> 事件。
        /// </summary>
        /// <param name="parcelId">包裹 Id。</param>
        /// <param name="updatedAt">清空时刻（本地时间）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>清空成功返回 true；包裹不存在返回 false。</returns>
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

        /// <summary>
        /// 标记包裹已落格，并触发 <see cref="ParcelDropped"/> 事件。
        /// </summary>
        /// <param name="parcelId">包裹 Id。</param>
        /// <param name="actualChuteId">实际落格的格口 Id，须大于 0。</param>
        /// <param name="droppedAt">落格时刻（本地时间）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>标记成功返回 true；包裹不存在或参数不合法返回 false。</returns>
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

        /// <summary>
        /// 从字典中移除指定包裹，并触发 <see cref="ParcelRemoved"/> 事件。
        /// </summary>
        /// <param name="parcelId">包裹 Id。</param>
        /// <param name="reason">移除原因（可选，用于日志）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>移除成功返回 true；包裹不存在返回 false。</returns>
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

        /// <summary>
        /// 清空全部包裹数据（幂等，并发安全）。
        /// </summary>
        /// <param name="reason">清空原因（可选，用于日志）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
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

        /// <summary>
        /// 尝试获取指定包裹的当前快照（无锁只读）。
        /// </summary>
        /// <param name="parcelId">包裹 Id。</param>
        /// <param name="parcel">找到时输出包裹信息；否则为 null。</param>
        /// <returns>存在返回 true，否则返回 false。</returns>
        public bool TryGet(long parcelId, out ParcelInfo parcel) {
            return _parcels.TryGetValue(parcelId, out parcel!);
        }

        /// <summary>
        /// 释放包裹字典资源。
        /// </summary>
        public void Dispose() {
            _parcels.Clear();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 根据包裹 Id 计算对应的条带锁对象（内联优化，避免方法调用开销）。
        /// </summary>
        /// <param name="parcelId">包裹 Id。</param>
        /// <returns>条带锁对象。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private object GetGate(long parcelId) {
            var h = (uint)parcelId ^ (uint)(parcelId >> 32);
            var idx = (int)(h & (uint)_gateMask);
            return _gates[idx];
        }

        /// <summary>
        /// 将时间归一化为本地时间，默认值替换为 <see cref="DateTime.Now"/>（内联优化）。
        /// </summary>
        /// <param name="value">输入时间。</param>
        /// <returns>归一化后的本地时间。</returns>
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

        /// <summary>
        /// 判断操作是否应被拒绝：正在清空、已取消或 parcelId 非法时返回 true 并记录日志。
        /// </summary>
        /// <param name="operation">操作名称（用于日志）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="parcelId">包裹 Id。</param>
        /// <returns>应拒绝返回 true，否则返回 false。</returns>
        private bool IsRejected(string operation, CancellationToken cancellationToken, long parcelId) {
            if (Volatile.Read(ref _isClearing) == 1 || cancellationToken.IsCancellationRequested || parcelId <= 0) {
                ParcelManagerLog.Rejected(_logger, operation, parcelId);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 更新包裹小车列表的通用实现（绑定/解绑），触发 <see cref="ParcelCarriersUpdated"/> 事件。
        /// </summary>
        /// <param name="parcelId">包裹 Id。</param>
        /// <param name="carrierId">小车 Id。</param>
        /// <param name="updatedAt">更新时刻。</param>
        /// <param name="changeType">变更类型。</param>
        /// <param name="updater">对包裹执行实际小车变更的委托。</param>
        /// <returns>更新成功返回 true；包裹不存在返回 false。</returns>
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

        /// <summary>
        /// 执行变更操作，捕获异常后触发 <see cref="Faulted"/> 事件并返回 false。
        /// </summary>
        /// <param name="operation">操作名称（用于异常日志）。</param>
        /// <param name="parcelId">包裹 Id（用于异常日志）。</param>
        /// <param name="mutation">实际执行的变更委托，返回是否成功。</param>
        /// <returns>变更成功返回 true；异常时返回 false。</returns>
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

        /// <summary>
        /// 记录异常日志并触发 <see cref="Faulted"/> 事件，向上层通知包裹管理器内部错误。
        /// </summary>
        /// <param name="operation">发生异常的操作名称。</param>
        /// <param name="parcelId">关联的包裹 Id（可为空）。</param>
        /// <param name="exception">捕获到的异常。</param>
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
