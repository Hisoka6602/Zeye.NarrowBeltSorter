using System.Runtime.CompilerServices;

namespace Zeye.NarrowBeltSorter.Core.Models.Parcel {
    /// <summary>
    /// 包裹信息（集合中长期驻留对象，支持高频原地更新）
    /// </summary>
    public sealed record class ParcelInfo {
        /// <summary>
        /// 目标格口快照读写互斥锁。
        /// </summary>
        private readonly object _targetChuteSync = new();

        /// <summary>
        /// 落格快照读写互斥锁。
        /// </summary>
        private readonly object _droppedSync = new();

        /// <summary>
        /// 目标格口 Id 字段。
        /// </summary>
        private long _targetChuteId;

        /// <summary>
        /// 目标格口更新时间（null 表示未设置）。
        /// </summary>
        private DateTime? _targetChuteUpdatedTime;

        /// <summary>
        /// 实际落格格口 Id 字段。
        /// </summary>
        private long? _actualChuteId;

        /// <summary>
        /// 落格时间字段。
        /// </summary>
        private DateTime _droppedTime;

        /// <summary>
        /// 包裹Id
        /// </summary>
        public required long ParcelId { get; init; }

        /// <summary>
        /// 包裹条码
        /// </summary>
        public string BarCode { get; init; } = string.Empty;

        /// <summary>
        /// 目标格口Id
        /// </summary>
        public long TargetChuteId {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                var snapshot = ReadTargetChuteSnapshot();
                return snapshot.TargetChuteId;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                SetTargetChute(value, DateTime.Now);
            }
        }

        /// <summary>
        /// 更新目标格口时间（null 表示未更新/未设置）
        /// </summary>
        public DateTime? TargetChuteUpdatedTime {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                var snapshot = ReadTargetChuteSnapshot();
                return snapshot.UpdatedTime;
            }
            internal set {
                // 步骤：仅更新时间字段，不触碰 _targetChuteId。
                // 原实现先读 TargetChuteId（释放锁），再写快照（再获取锁），
                // 两次加锁之间若有并发写入会静默回滚 _targetChuteId。
                lock (_targetChuteSync) {
                    _targetChuteUpdatedTime = value;
                }
            }
        }

        // 环形分拣：包裹可能在环线上被多次绑定/重绑定到不同小车，故使用“绑定小车Id集合”
        // 读：无锁（volatile 读取快照数组）
        // 写：仅在绑定/解绑时进入锁，避免高频读被影响
        private long[] _carrierIds = [];
        private readonly object _carrierSync = new();

        /// <summary>
        /// 绑定的小车Id集合（快照；不会返回可写集合）
        /// </summary>
        public IReadOnlyList<long> CarrierIds {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Volatile.Read(ref _carrierIds);
        }

        /// <summary>
        /// 获取目标格口与更新时间的成组快照。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (long TargetChuteId, DateTime? UpdatedTime) GetTargetChuteSnapshot() {
            return ReadTargetChuteSnapshot();
        }

        /// <summary>
        /// 获取实际落格格口与落格时间的成组快照。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (long? ActualChuteId, DateTime DroppedTime) GetDroppedSnapshot() {
            return ReadDroppedSnapshot();
        }

        /// <summary>
        /// 是否已落格
        /// </summary>
        public bool IsDropped {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                var snapshot = ReadDroppedSnapshot();
                return snapshot.ActualChuteId.HasValue;
            }
        }

        /// <summary>
        /// 实际落格格口Id（未落格时为 null）。
        /// </summary>
        public long? ActualChuteId {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                var snapshot = ReadDroppedSnapshot();
                return snapshot.ActualChuteId;
            }
        }

        /// <summary>
        /// 落格时间（默认值表示未落格/未设置）。
        /// </summary>
        public DateTime DroppedTime {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                var snapshot = ReadDroppedSnapshot();
                return snapshot.DroppedTime;
            }
        }

        /// <summary>
        /// 绑定小车
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BindCarrier(long carrierId) {
            if (carrierId <= 0) {
                throw new ArgumentOutOfRangeException(nameof(carrierId), "参数无效：carrierId 必须为正数。");
            }

            lock (_carrierSync) {
                var snapshot = _carrierIds;
                if (snapshot.Length == 0) {
                    _carrierIds = new[] { carrierId };
                    return;
                }

                foreach (var t in snapshot) {
                    if (t == carrierId) {
                        return;
                    }
                }

                var next = new long[snapshot.Length + 1];
                Array.Copy(snapshot, next, snapshot.Length);
                next[^1] = carrierId;
                _carrierIds = next;
            }
        }

        /// <summary>
        /// 解绑小车
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UnbindCarrier(long carrierId) {
            if (carrierId <= 0) {
                throw new ArgumentOutOfRangeException(nameof(carrierId), "参数无效：carrierId 必须为正数。");
            }

            lock (_carrierSync) {
                var snapshot = _carrierIds;
                if (snapshot.Length == 0) {
                    return;
                }

                var index = -1;
                for (var i = 0; i < snapshot.Length; i++) {
                    if (snapshot[i] == carrierId) {
                        index = i;
                        break;
                    }
                }

                if (index < 0) {
                    return;
                }

                if (snapshot.Length == 1) {
                    _carrierIds = [];
                    return;
                }

                var next = new long[snapshot.Length - 1];
                if (index > 0) {
                    Array.Copy(snapshot, 0, next, 0, index);
                }

                if (index < snapshot.Length - 1) {
                    Array.Copy(snapshot, index + 1, next, index, snapshot.Length - index - 1);
                }

                _carrierIds = next;
            }
        }

        /// <summary>
        /// 清空绑定小车集合
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearCarriers() {
            lock (_carrierSync) {
                if (_carrierIds.Length == 0) {
                    return;
                }

                _carrierIds = [];
            }
        }

        /// <summary>
        /// 标记落格（通常只调用一次；重复调用会覆盖）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkDropped(long actualChuteId, DateTime droppedTime) {
            if (actualChuteId <= 0) {
                throw new ArgumentOutOfRangeException(nameof(actualChuteId), "参数无效：actualChuteId 必须为正数。");
            }

            var effectiveDroppedTime = droppedTime == default ? DateTime.Now : droppedTime;
            WriteDroppedSnapshot(actualChuteId, effectiveDroppedTime);
        }

        /// <summary>
        /// 以成组一致方式设置目标格口与更新时间。
        /// </summary>
        /// <param name="targetChuteId">目标格口 Id。</param>
        /// <param name="updatedTime">更新时间（本地时间语义）。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTargetChute(long targetChuteId, DateTime updatedTime) {
            if (targetChuteId < 0) {
                throw new ArgumentOutOfRangeException(nameof(targetChuteId), "参数无效：targetChuteId 不能为负数。");
            }

            var effectiveUpdatedTime = updatedTime == default ? DateTime.Now : updatedTime;
            WriteTargetChuteSnapshot(targetChuteId, effectiveUpdatedTime, skipIfUnchanged: true);
        }

        /// <summary>
        /// 读取目标格口稳定快照，避免读取到半更新状态。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private (long TargetChuteId, DateTime? UpdatedTime) ReadTargetChuteSnapshot() {
            lock (_targetChuteSync) {
                // 步骤 1：在同一锁内读取成组字段，保证快照一致性。
                return (_targetChuteId, _targetChuteUpdatedTime);
            }
        }

        /// <summary>
        /// 以成组一致方式写入目标格口快照。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteTargetChuteSnapshot(long targetChuteId, DateTime? updatedTime, bool skipIfUnchanged) {
            lock (_targetChuteSync) {
                // 步骤 1：在需要保持“值不变不刷新时间”语义时，先判断目标格口是否变化。
                if (skipIfUnchanged && _targetChuteId == targetChuteId) {
                    return;
                }

                // 步骤 2：以同一临界区完成成组字段写入，避免外部读到半更新状态。
                _targetChuteId = targetChuteId;
                _targetChuteUpdatedTime = updatedTime;
            }
        }

        /// <summary>
        /// 读取落格稳定快照，避免读取到半更新状态。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private (long? ActualChuteId, DateTime DroppedTime) ReadDroppedSnapshot() {
            lock (_droppedSync) {
                // 步骤 1：在同一锁内读取成组字段，保证快照一致性。
                return (_actualChuteId, _droppedTime);
            }
        }

        /// <summary>
        /// 以成组一致方式写入落格快照。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteDroppedSnapshot(long actualChuteId, DateTime droppedTime) {
            lock (_droppedSync) {
                // 步骤 1：以同一临界区完成成组字段写入，避免外部读到半更新状态。
                _actualChuteId = actualChuteId;
                _droppedTime = droppedTime;
            }
        }
    }
}
