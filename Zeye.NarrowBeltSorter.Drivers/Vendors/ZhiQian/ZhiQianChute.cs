using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.Chutes;
using Zeye.NarrowBeltSorter.Core.Enums.Carrier;
using Zeye.NarrowBeltSorter.Core.Events.Chutes;
using Zeye.NarrowBeltSorter.Core.Models.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.ZhiQian {

    /// <summary>
    /// 智嵌继电器格口实现（纯内存状态，IO 状态由 ZhiQianChuteManager 根据 DO 写入结果同步）。
    /// </summary>
    internal sealed class ZhiQianChute : IChute {
        private readonly object _lock = new();

        private ChuteStatus _status = ChuteStatus.Idle;
        private bool _isForced;
        private bool _isTarget;
        private ParcelInfo? _waitingParcel;
        private DateTime? _expectedDropAt;
        private readonly List<ParcelInfo> _droppedParcels = new();
        private long _droppedCount;
        private IoState _ioState = IoState.Low;
        private int _distanceToCarrierIoCount;
        private TimeSpan _dropDelayCompensation;

        private IReadOnlyDictionary<ParcelToChuteDistanceLevel, TimeSpan> _distanceCompensationMap
            = new Dictionary<ParcelToChuteDistanceLevel, TimeSpan>();

        private (DateTime OpenAt, DateTime CloseAt)? _lastIoOpenCloseWindow;
        private (DateTime OpenAt, DateTime CloseAt)? _pendingIoOpenCloseWindow;

        /// <summary>
        /// 初始化格口实例。
        /// </summary>
        /// <param name="id">格口 Id。</param>
        /// <param name="name">格口名称。</param>
        public ZhiQianChute(long id, string name) {
            Id = id;
            Name = name;
        }

        /// <inheritdoc />
        public long Id { get; }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public bool IsForced {
            get { lock (_lock) { return _isForced; } }
        }

        /// <inheritdoc />
        public ChuteStatus Status {
            get { lock (_lock) { return _status; } }
        }

        /// <inheritdoc />
        public bool IsTarget {
            get { lock (_lock) { return _isTarget; } }
        }

        public decimal CarrierSpeed { get; }
        public ushort Din { get; }
        public CarrierTurnDirection Direction { get; }

        /// <inheritdoc />
        public ParcelInfo? WaitingParcel {
            get { lock (_lock) { return _waitingParcel; } }
        }

        /// <inheritdoc />
        public DateTime? ExpectedDropAt {
            get { lock (_lock) { return _expectedDropAt; } }
        }

        /// <inheritdoc />
        public IReadOnlyCollection<ParcelInfo> DroppedParcels {
            get { lock (_lock) { return _droppedParcels.ToArray(); } }
        }

        /// <inheritdoc />
        public long DroppedCount {
            get { lock (_lock) { return _droppedCount; } }
        }

        /// <inheritdoc />
        public IoState IoState {
            get { lock (_lock) { return _ioState; } }
        }

        /// <inheritdoc />
        public int DistanceToCarrierIoCount {
            get { lock (_lock) { return _distanceToCarrierIoCount; } }
        }

        /// <inheritdoc />
        public TimeSpan DropDelayCompensation {
            get { lock (_lock) { return _dropDelayCompensation; } }
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<ParcelToChuteDistanceLevel, TimeSpan> DistanceCompensationMap {
            get { lock (_lock) { return _distanceCompensationMap; } }
        }

        /// <inheritdoc />
        public (DateTime OpenAt, DateTime CloseAt)? LastIoOpenCloseWindow {
            get { lock (_lock) { return _lastIoOpenCloseWindow; } }
        }

        /// <inheritdoc />
        public (DateTime OpenAt, DateTime CloseAt)? PendingIoOpenCloseWindow {
            get { lock (_lock) { return _pendingIoOpenCloseWindow; } }
        }

        /// <inheritdoc />
        public event EventHandler<ChuteStatusChangedEventArgs>? StatusChanged;

        /// <inheritdoc />
        public event EventHandler<ChuteParcelDroppedEventArgs>? ParcelDropped;

        /// <inheritdoc />
        public event EventHandler<ChuteIoStateChangedEventArgs>? IoStateChanged;

        /// <inheritdoc />
        public event EventHandler<ChuteDropDelayCompensationChangedEventArgs>? DropDelayCompensationChanged;

        /// <inheritdoc />
        public event EventHandler<ChuteDistanceCompensationChangedEventArgs>? DistanceCompensationChanged;

        /// <inheritdoc />
        public ValueTask<bool> SetStatusAsync(
            ChuteStatus status,
            string? reason = null,
            CancellationToken cancellationToken = default) {
            ChuteStatus old;
            lock (_lock) {
                old = _status;
                _status = status;
            }

            if (old != status) {
                StatusChanged?.Invoke(this, new ChuteStatusChangedEventArgs {
                    ChuteId = Id,
                    OldStatus = old,
                    NewStatus = status,
                    ChangedAt = DateTime.Now,
                    Reason = reason
                });
            }

            return ValueTask.FromResult(true);
        }

        /// <inheritdoc />
        public ValueTask<bool> SetWaitingParcelAsync(
            ParcelInfo? parcel,
            CancellationToken cancellationToken = default) {
            lock (_lock) {
                _waitingParcel = parcel;
            }

            return ValueTask.FromResult(true);
        }

        /// <inheritdoc />
        public ValueTask<bool> SetExpectedDropAtAsync(
            DateTime? expectedDropAt,
            CancellationToken cancellationToken = default) {
            lock (_lock) {
                _expectedDropAt = expectedDropAt;
            }

            return ValueTask.FromResult(true);
        }

        /// <inheritdoc />
        public ValueTask<bool> SetDistanceToCarrierIoCountAsync(
            int distanceToCarrierIoCount,
            CancellationToken cancellationToken = default) {
            if (distanceToCarrierIoCount < 0) {
                return ValueTask.FromResult(false);
            }

            lock (_lock) {
                _distanceToCarrierIoCount = distanceToCarrierIoCount;
            }

            return ValueTask.FromResult(true);
        }

        /// <inheritdoc />
        public ValueTask<bool> SetDropDelayCompensationAsync(
            TimeSpan compensation,
            string? reason = null,
            CancellationToken cancellationToken = default) {
            TimeSpan old;
            lock (_lock) {
                old = _dropDelayCompensation;
                _dropDelayCompensation = compensation;
            }

            if (old != compensation) {
                DropDelayCompensationChanged?.Invoke(this, new ChuteDropDelayCompensationChangedEventArgs {
                    ChuteId = Id,
                    OldCompensation = old,
                    NewCompensation = compensation,
                    ChangedAt = DateTime.Now,
                    Reason = reason
                });
            }

            return ValueTask.FromResult(true);
        }

        /// <inheritdoc />
        public ValueTask<bool> SetDistanceCompensationAsync(
            IReadOnlyDictionary<ParcelToChuteDistanceLevel, TimeSpan>? compensationMap,
            string? reason = null,
            CancellationToken cancellationToken = default) {
            if (compensationMap is null) {
                return ValueTask.FromResult(false);
            }

            IReadOnlyDictionary<ParcelToChuteDistanceLevel, TimeSpan> old;
            lock (_lock) {
                old = _distanceCompensationMap;
                _distanceCompensationMap = compensationMap;
            }

            DistanceCompensationChanged?.Invoke(this, new ChuteDistanceCompensationChangedEventArgs {
                ChuteId = Id,
                OldCompensationMap = old,
                NewCompensationMap = compensationMap,
                ChangedAt = DateTime.Now,
                Reason = reason
            });
            return ValueTask.FromResult(true);
        }

        /// <inheritdoc />
        public ValueTask<bool> DropAsync(
            ParcelInfo parcel,
            DateTime droppedAt,
            CancellationToken cancellationToken = default) {
            lock (_lock) {
                _droppedParcels.Add(parcel);
                _droppedCount++;
                _waitingParcel = null;
                _expectedDropAt = null;
            }

            ParcelDropped?.Invoke(this, new ChuteParcelDroppedEventArgs {
                ChuteId = Id,
                ParcelId = parcel.ParcelId,
                DroppedAt = droppedAt
            });
            return ValueTask.FromResult(true);
        }

        /// <inheritdoc />
        public ValueTask<bool> EnableForceOpenAsync(
            bool enabled,
            CancellationToken cancellationToken = default) {
            lock (_lock) {
                _isForced = enabled;
            }

            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> SetCarrierMotionAsync(CarrierTurnDirection direction, decimal speed,
            CancellationToken cancellationToken = default) {
            //从小车指令中获取小车运动状态，暂不处理

            return default;
        }

        /// <summary>
        /// 同步 IO 状态（由 ZhiQianChuteManager 在 DO 写入/轮询后调用）。
        /// </summary>
        /// <param name="ioState">新的 IO 状态。</param>
        internal void SyncIoState(IoState ioState) {
            IoState old;
            lock (_lock) {
                old = _ioState;
                _ioState = ioState;
            }

            if (old != ioState) {
                IoStateChanged?.Invoke(this, new ChuteIoStateChangedEventArgs {
                    ChuteId = Id,
                    OldState = old,
                    NewState = ioState,
                    ChangedAt = DateTime.Now
                });
            }
        }

        /// <summary>
        /// 设置是否为目标格口（由 ZhiQianChuteManager 管理）。
        /// </summary>
        /// <param name="isTarget">是否目标格口。</param>
        internal void SetIsTarget(bool isTarget) {
            lock (_lock) {
                _isTarget = isTarget;
            }
        }

        /// <summary>
        /// 设置 IO 开闭时窗记录（由 ZhiQianChuteManager 管理）。
        /// </summary>
        /// <param name="openAt">开闸本地时间。</param>
        /// <param name="closeAt">关闸本地时间。</param>
        internal void SetPendingIoWindow(DateTime openAt, DateTime closeAt) {
            lock (_lock) {
                _pendingIoOpenCloseWindow = (openAt, closeAt);
            }
        }

        /// <summary>
        /// 将当前待执行时窗迁移为已执行时窗（由 ZhiQianChuteManager 在执行后调用）。
        /// </summary>
        internal void CommitIoWindow() {
            lock (_lock) {
                if (_pendingIoOpenCloseWindow.HasValue) {
                    _lastIoOpenCloseWindow = _pendingIoOpenCloseWindow;
                    _pendingIoOpenCloseWindow = null;
                }
            }
        }
    }
}
