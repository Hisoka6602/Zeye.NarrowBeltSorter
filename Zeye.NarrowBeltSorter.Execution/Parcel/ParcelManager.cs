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
        /// 创建包裹记录。
        /// </summary>
        /// <param name="parcel">包裹实体。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>创建成功返回 true，否则返回 false。</returns>
        public ValueTask<bool> CreateAsync(ParcelInfo parcel, CancellationToken cancellationToken = default) {
            // 步骤1：先处理空参数并写入拒绝日志，避免无声失败。
            if (parcel is null) {
                ParcelManagerLog.Rejected(_logger, "CreateAsync", 0);
                return ValueTask.FromResult(false);
            }

            // 步骤2：统一执行拒绝条件校验（清空中、取消或非法编号）。
            if (IsRejected("CreateAsync", cancellationToken, parcel.ParcelId)) {
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
        /// 分配目标格口。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="targetChuteId">目标格口编号。</param>
        /// <param name="assignedAt">分配时间。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>更新成功返回 true，否则返回 false。</returns>
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
        /// 绑定小车到指定包裹。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="carrierId">小车编号。</param>
        /// <param name="updatedAt">更新时间。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>绑定成功返回 true，否则返回 false。</returns>
        public ValueTask<bool> BindCarrierAsync(long parcelId, long carrierId, DateTime updatedAt, CancellationToken cancellationToken = default) {
            if (IsRejected("BindCarrierAsync", cancellationToken, parcelId) || carrierId <= 0) {
                return ValueTask.FromResult(false);
            }

            return UpdateCarriersAsync(parcelId, carrierId, updatedAt, ParcelCarriersChangeType.Bound, (parcel, id) => {
                parcel.BindCarrier(id);
            });
        }

        /// <summary>
        /// 从指定包裹解绑小车。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="carrierId">小车编号。</param>
        /// <param name="updatedAt">更新时间。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>解绑成功返回 true，否则返回 false。</returns>
        public ValueTask<bool> UnbindCarrierAsync(long parcelId, long carrierId, DateTime updatedAt, CancellationToken cancellationToken = default) {
            if (IsRejected("UnbindCarrierAsync", cancellationToken, parcelId) || carrierId <= 0) {
                return ValueTask.FromResult(false);
            }

            return UpdateCarriersAsync(parcelId, carrierId, updatedAt, ParcelCarriersChangeType.Unbound, (parcel, id) => {
                parcel.UnbindCarrier(id);
            });
        }

        /// <summary>
        /// 替换包裹绑定的小车集合。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="carrierIds">目标小车编号集合。</param>
        /// <param name="updatedAt">更新时间。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>替换成功返回 true，否则返回 false。</returns>
        public ValueTask<bool> ReplaceCarriersAsync(long parcelId, IReadOnlyList<long> carrierIds, DateTime updatedAt, CancellationToken cancellationToken = default) {
            // 步骤1：统一执行入参合法性与拒绝条件校验。
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
        /// 清空包裹绑定的小车集合。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="updatedAt">更新时间。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>清空成功返回 true，否则返回 false。</returns>
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
        /// 标记包裹已落格。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="actualChuteId">实际落格口编号。</param>
        /// <param name="droppedAt">落格时间。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>标记成功返回 true，否则返回 false。</returns>
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
        /// 移除包裹记录。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="reason">移除原因。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>移除成功返回 true，否则返回 false。</returns>
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
        /// 清空全部包裹记录。
        /// </summary>
        /// <param name="reason">清空原因。</param>
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
        /// 按编号获取包裹信息。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="parcel">输出包裹对象。</param>
        /// <returns>命中返回 true，否则返回 false。</returns>
        public bool TryGet(long parcelId, out ParcelInfo parcel) {
            return _parcels.TryGetValue(parcelId, out parcel!);
        }

        /// <summary>
        /// 释放管理器资源。
        /// </summary>
        public void Dispose() {
            _parcels.Clear();
            GC.SuppressFinalize(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        /// <summary>
        /// 获取包裹编号对应的分段锁对象。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <returns>分段锁对象。</returns>
        private object GetGate(long parcelId) {
            var h = (uint)parcelId ^ (uint)(parcelId >> 32);
            var idx = (int)(h & (uint)_gateMask);
            return _gates[idx];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        /// <summary>
        /// 规范化为本地时间语义。
        /// </summary>
        /// <param name="value">输入时间。</param>
        /// <returns>规范化后的时间。</returns>
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
        /// 判断当前操作是否需要拒绝执行。
        /// </summary>
        /// <param name="operation">操作名。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="parcelId">包裹编号。</param>
        /// <returns>需要拒绝返回 true，否则返回 false。</returns>
        private bool IsRejected(string operation, CancellationToken cancellationToken, long parcelId) {
            if (Volatile.Read(ref _isClearing) == 1 || cancellationToken.IsCancellationRequested || parcelId <= 0) {
                ParcelManagerLog.Rejected(_logger, operation, parcelId);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 更新包裹小车绑定关系。
        /// </summary>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="carrierId">小车编号。</param>
        /// <param name="updatedAt">更新时间。</param>
        /// <param name="changeType">变更类型。</param>
        /// <param name="updater">具体更新动作。</param>
        /// <returns>更新成功返回 true，否则返回 false。</returns>
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
        /// 执行带统一异常收敛的写操作。
        /// </summary>
        /// <param name="operation">操作名。</param>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="mutation">写操作委托。</param>
        /// <returns>执行成功返回 true，否则返回 false。</returns>
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
        /// 触发故障事件并记录异常日志。
        /// </summary>
        /// <param name="operation">操作名。</param>
        /// <param name="parcelId">包裹编号。</param>
        /// <param name="exception">异常对象。</param>
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

        /// <summary>
        /// 通过统一安全执行器触发事件。
        /// </summary>
        /// <typeparam name="TArgs">事件参数类型。</typeparam>
        /// <param name="handler">事件处理器。</param>
        /// <param name="args">事件参数。</param>
        private void RaiseSafe<TArgs>(EventHandler<TArgs>? handler, TArgs args) {
            if (handler is null) {
                return;
            }

            _safeExecutor.Execute(() => {
                handler.Invoke(this, args);
            }, "ParcelManager.EventDispatch");
        }
    }
}
