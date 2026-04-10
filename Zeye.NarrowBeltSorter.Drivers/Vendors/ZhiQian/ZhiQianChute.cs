using NLog;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.Chutes;
using Zeye.NarrowBeltSorter.Core.Enums.Carrier;
using Zeye.NarrowBeltSorter.Core.Events.Chutes;
using Zeye.NarrowBeltSorter.Core.Models.Parcel;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Protocols;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.ZhiQian {

    /// <summary>
    /// 智嵌继电器格口实现，负责维护格口业务状态并通过适配器下发红外驱动帧执行 IO 控制，
    /// 与 ZhiQianChuteManager 协同完成格口 IO 状态与业务状态同步。
    /// </summary>
    internal sealed class ZhiQianChute : IChute {
        private static readonly Logger Log = LogManager.GetLogger(nameof(ZhiQianChute));
        private readonly object _lock = new();
        private readonly IZhiQianClientAdapter _adapter;
        private readonly IInfraredDriverFrameCodec _infraredDriverFrameCodec;
        private readonly SafeExecutor _safeExecutor;
        private readonly int _doIndex;

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
        private InfraredChuteOptions _infraredChuteOptions;

        /// <summary>
        /// 初始化格口实例。
        /// </summary>
        /// <param name="id">格口 Id。</param>
        /// <param name="name">格口名称。</param>
        /// <param name="doIndex"></param>
        /// <param name="infraredChuteOptions"></param>
        /// <param name="adapter"></param>
        /// <param name="infraredDriverFrameCodec"></param>
        /// <param name="safeExecutor"></param>
        public ZhiQianChute(
            long id,
            string name,
            int doIndex,
            InfraredChuteOptions infraredChuteOptions,
            IZhiQianClientAdapter adapter,
            IInfraredDriverFrameCodec infraredDriverFrameCodec,
            SafeExecutor safeExecutor) {
            Id = id;
            Name = name;
            _doIndex = doIndex;
            _infraredChuteOptions = infraredChuteOptions ?? throw new ArgumentNullException(nameof(infraredChuteOptions));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _infraredDriverFrameCodec = infraredDriverFrameCodec ?? throw new ArgumentNullException(nameof(infraredDriverFrameCodec));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
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

        /// <inheritdoc />
        public InfraredChuteOptions InfraredChuteOptions {
            get { lock (_lock) { return _infraredChuteOptions; } }
        }

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
                _safeExecutor.PublishEventAsync(StatusChanged, this, new ChuteStatusChangedEventArgs {
                    ChuteId = Id,
                    OldStatus = old,
                    NewStatus = status,
                    ChangedAt = DateTime.Now,
                    Reason = reason
                }, "ZhiQianChute.StatusChanged");
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
                _safeExecutor.PublishEventAsync(DropDelayCompensationChanged, this, new ChuteDropDelayCompensationChangedEventArgs {
                    ChuteId = Id,
                    OldCompensation = old,
                    NewCompensation = compensation,
                    ChangedAt = DateTime.Now,
                    Reason = reason
                }, "ZhiQianChute.DropDelayCompensationChanged");
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

            _safeExecutor.PublishEventAsync(DistanceCompensationChanged, this, new ChuteDistanceCompensationChangedEventArgs {
                ChuteId = Id,
                OldCompensationMap = old,
                NewCompensationMap = compensationMap,
                ChangedAt = DateTime.Now,
                Reason = reason
            }, "ZhiQianChute.DistanceCompensationChanged");
            return ValueTask.FromResult(true);
        }

        /// <inheritdoc />
        public async ValueTask<bool> DropAsync(
            ParcelInfo? parcel,
            DateTime droppedAt,
            TimeSpan openCloseInterval,
            CancellationToken cancellationToken = default) {
            if (parcel is null) {
                return false;
            }

            if (openCloseInterval < TimeSpan.Zero) {
                return false;
            }

            var shouldToggleIo = true;
            var isForced = false;
            lock (_lock) {
                isForced = _isForced;
            }

            if (isForced) {
                shouldToggleIo = false;
            }

            if (shouldToggleIo) {
                var execution = await _safeExecutor.ExecuteAsync(
                    async ct => {
                        await _adapter.WriteSingleDoAsync(_doIndex, true, ct).ConfigureAwait(false);
                        SyncIoState(IoState.High);

                        if (openCloseInterval > TimeSpan.Zero) {
                            await Task.Delay(openCloseInterval, ct).ConfigureAwait(false);
                        }

                        await _adapter.WriteSingleDoAsync(_doIndex, false, ct).ConfigureAwait(false);
                        SyncIoState(IoState.Low);
                    },
                    $"ZhiQianChute.DropAsync.ToggleIo[{Id}]",
                    cancellationToken,
                    ex => Log.Error(ex, "智嵌格口开闭失败 chuteId={0}", Id)).ConfigureAwait(false);

                if (!execution) {
                    return false;
                }
            }

            lock (_lock) {
                _droppedParcels.Add(parcel);
                _droppedCount++;
                _waitingParcel = null;
                _expectedDropAt = null;
            }

            _safeExecutor.PublishEventAsync(ParcelDropped, this, new ChuteParcelDroppedEventArgs {
                ChuteId = Id,
                ParcelId = parcel.ParcelId,
                DroppedAt = droppedAt
            }, "ZhiQianChute.ParcelDropped");
            return await ValueTask.FromResult(true);
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

        /// <inheritdoc />
        /// <remarks>
        /// 步骤1：使用红外驱动帧编解码器将参数编码为设备帧。
        /// 步骤2：通过智嵌链路发送编码帧。
        /// 步骤3：发送成功后更新当前格口红外参数快照。
        /// </remarks>
        public async ValueTask<bool> WriteInfraredChuteOptionsAsync(InfraredChuteOptions options, string? reason = null,
            CancellationToken cancellationToken = default) {
            var execution = await _safeExecutor.ExecuteAsync(
                async ct => {
                    // 步骤1：通过红外帧编解码器将参数编码为设备指令。
                    var (encoded, frame) = await _infraredDriverFrameCodec.EncodeAsync(options, ct).ConfigureAwait(false);
                    if (!encoded || frame.IsEmpty) {
                        return false;
                    }

                    // 步骤2：将编码后的红外帧经智嵌链路下发。
                    await _adapter.WriteInfraredFrameAsync(frame, ct).ConfigureAwait(false);

                    // 步骤3：下发成功后更新本地快照。
                    lock (_lock) {
                        _infraredChuteOptions = options;
                    }

                    return true;
                },
                $"ZhiQianChute.WriteInfraredChuteOptionsAsync[{Id}]",
                false,
                cancellationToken,
                ex => Log.Error(
                    ex,
                    "智嵌写入红外参数失败 chuteId={0} reason={1}",
                    Id,
                    string.IsNullOrWhiteSpace(reason) ? "未提供" : reason)).ConfigureAwait(false);

            return execution.Success;
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
                _safeExecutor.PublishEventAsync(IoStateChanged, this, new ChuteIoStateChangedEventArgs {
                    ChuteId = Id,
                    OldState = old,
                    NewState = ioState,
                    ChangedAt = DateTime.Now
                }, "ZhiQianChute.IoStateChanged");
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
