using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.Device;
using Zeye.NarrowBeltSorter.Core.Enums.Carrier;
using Zeye.NarrowBeltSorter.Core.Models.Parcel;
using Zeye.NarrowBeltSorter.Core.Events.Carrier;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;

namespace Zeye.NarrowBeltSorter.Execution.Services.Carrier {

    /// <summary>
    /// 红外感应器模式下的内存态小车模型（仅用于计算，不具备硬件控制能力）。
    /// </summary>
    public sealed class InfraredSensorCarrier : ICarrier {
        private readonly SafeExecutor _safeExecutor;

        /// <summary>
        /// 状态字段互斥锁：保护多步状态写入的原子性，防止并发读到半更新状态。
        /// </summary>
        private readonly object _sync = new();

        /// <summary>
        /// 是否已装载包裹（volatile，支持热路径无锁读取）。
        /// </summary>
        private volatile bool _isLoaded;

        private ParcelInfo? _parcel;
        private IReadOnlyList<long> _linkedCarrierIds = [];
        private decimal _speed;
        private CarrierTurnDirection _turnDirection = CarrierTurnDirection.Left;
        private DeviceConnectionStatus _connectionStatus = DeviceConnectionStatus.Connected;

        /// <summary>
        /// 初始化红外小车内存模型。
        /// </summary>
        /// <param name="id">小车编号。</param>
        /// <param name="safeExecutor">统一安全执行器。</param>
        public InfraredSensorCarrier(long id, SafeExecutor safeExecutor) {
            Id = id;
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
        }

        public long Id { get; }

        /// <summary>速度（毫米每秒）。</summary>
        public decimal Speed { get { lock (_sync) { return _speed; } } }

        /// <summary>当前转向。</summary>
        public CarrierTurnDirection TurnDirection { get { lock (_sync) { return _turnDirection; } } }

        /// <summary>设备连接状态。</summary>
        public DeviceConnectionStatus ConnectionStatus { get { lock (_sync) { return _connectionStatus; } } }

        /// <summary>
        /// 是否已装载包裹（volatile 读，供热路径快速判断；写在 _sync 锁内保证多步原子性）。
        /// </summary>
        public bool IsLoaded => _isLoaded;

        /// <summary>当前装载的包裹信息。</summary>
        public ParcelInfo? Parcel { get { lock (_sync) { return _parcel; } } }

        /// <summary>关联小车 Id 列表。</summary>
        public IReadOnlyList<long> LinkedCarrierIds { get { lock (_sync) { return _linkedCarrierIds; } } }

        /// <summary>是否被其他小车关联。</summary>
        public bool IsLinkedByOther { get { lock (_sync) { return _linkedCarrierIds.Count > 0; } } }

        public event EventHandler<CarrierConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

        public event EventHandler<CarrierLoadStatusChangedEventArgs>? LoadStatusChanged;

        public event EventHandler<CarrierTurnDirectionChangedEventArgs>? TurnDirectionChanged;

        public event EventHandler<CarrierSpeedChangedEventArgs>? SpeedChanged;

        /// <summary>
        /// 连接小车（内存实现始终成功）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否成功。</returns>
        public ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            DeviceConnectionStatus oldStatus, newStatus;
            // 步骤：在锁内完成读-改-写，保证连接状态变更对所有线程可见且不出现中间态。
            lock (_sync) {
                oldStatus = _connectionStatus;
                _connectionStatus = DeviceConnectionStatus.Connected;
                newStatus = _connectionStatus;
            }

            if (oldStatus != newStatus) {
                _safeExecutor.PublishEventAsync(ConnectionStatusChanged, this, new CarrierConnectionStatusChangedEventArgs {
                    CarrierId = Id,
                    OldStatus = oldStatus,
                    NewStatus = newStatus,
                    ChangedAt = DateTime.Now,
                }, "InfraredSensorCarrier.ConnectionStatusChanged");
            }

            return ValueTask.FromResult(true);
        }

        /// <summary>
        /// 断开小车（内存实现始终成功）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否成功。</returns>
        public ValueTask<bool> DisconnectAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            DeviceConnectionStatus oldStatus, newStatus;
            // 步骤：在锁内完成读-改-写，保证连接状态变更对所有线程可见且不出现中间态。
            lock (_sync) {
                oldStatus = _connectionStatus;
                _connectionStatus = DeviceConnectionStatus.Disconnected;
                newStatus = _connectionStatus;
            }

            if (oldStatus != newStatus) {
                _safeExecutor.PublishEventAsync(ConnectionStatusChanged, this, new CarrierConnectionStatusChangedEventArgs {
                    CarrierId = Id,
                    OldStatus = oldStatus,
                    NewStatus = newStatus,
                    ChangedAt = DateTime.Now,
                }, "InfraredSensorCarrier.ConnectionStatusChanged");
            }

            return ValueTask.FromResult(true);
        }

        /// <summary>
        /// 设置小车转向。
        /// </summary>
        /// <param name="turnDirection">目标转向。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否成功。</returns>
        public ValueTask<bool> SetTurnDirectionAsync(CarrierTurnDirection turnDirection, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            CarrierTurnDirection oldDirection;
            // 步骤：在锁内完成读-改-写，保证转向状态变更对所有线程可见且不出现中间态。
            lock (_sync) {
                oldDirection = _turnDirection;
                _turnDirection = turnDirection;
            }

            if (oldDirection != turnDirection) {
                _safeExecutor.PublishEventAsync(TurnDirectionChanged, this, new CarrierTurnDirectionChangedEventArgs {
                    CarrierId = Id,
                    OldDirection = oldDirection,
                    NewDirection = turnDirection,
                    ChangedAt = DateTime.Now,
                }, "InfraredSensorCarrier.TurnDirectionChanged");
            }

            return ValueTask.FromResult(true);
        }

        /// <summary>
        /// 设置小车速度。
        /// </summary>
        /// <param name="speedMmps">速度（毫米每秒）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否成功。</returns>
        public ValueTask<bool> SetSpeedAsync(decimal speedMmps, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            decimal oldSpeed;
            // 步骤：在锁内完成读-改-写，保证速度变更对所有线程可见且不出现中间态。
            lock (_sync) {
                oldSpeed = _speed;
                _speed = speedMmps;
            }

            if (oldSpeed != speedMmps) {
                _safeExecutor.PublishEventAsync(SpeedChanged, this, new CarrierSpeedChangedEventArgs {
                    CarrierId = Id,
                    OldSpeed = oldSpeed,
                    NewSpeed = speedMmps,
                    ChangedAt = DateTime.Now,
                }, "InfraredSensorCarrier.SpeedChanged");
            }

            return ValueTask.FromResult(true);
        }

        /// <summary>
        /// 装载包裹。
        /// </summary>
        /// <param name="parcel">包裹信息。</param>
        /// <param name="linkedCarrierIds">关联小车列表。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否成功。</returns>
        public ValueTask<bool> LoadParcelAsync(
            ParcelInfo parcel,
            IReadOnlyList<long> linkedCarrierIds,
            CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            bool oldLoaded;
            // 步骤：在锁内以原子方式完成三字段联合写入（Parcel、LinkedCarrierIds、IsLoaded），
            // 确保其他线程不会读到半更新状态（如 IsLoaded=false 但 Parcel 已写入）。
            // IsLoaded（volatile）最后写入，作为可见性发布屏障（publication idiom）。
            lock (_sync) {
                oldLoaded = _isLoaded;
                _parcel = parcel;
                _linkedCarrierIds = linkedCarrierIds;
                _isLoaded = true;
            }

            if (!oldLoaded) {
                _safeExecutor.PublishEventAsync(LoadStatusChanged, this, new CarrierLoadStatusChangedEventArgs {
                    CarrierId = Id,
                    OldIsLoaded = false,
                    NewIsLoaded = true,
                    CurrentInductionCarrierId = null,
                    ChangedAt = DateTime.Now,
                }, "InfraredSensorCarrier.LoadStatusChanged");
            }

            return ValueTask.FromResult(true);
        }

        /// <summary>
        /// 卸载包裹。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否成功。</returns>
        public ValueTask<bool> UnloadParcelAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            bool oldLoaded;
            // 步骤：在锁内以原子方式完成三字段联合清零（IsLoaded 先置 false，再清 Parcel/LinkedCarrierIds），
            // 确保其他线程不会读到 IsLoaded=false 但 Parcel 仍为旧值的中间态。
            lock (_sync) {
                oldLoaded = _isLoaded;
                _isLoaded = false;
                _parcel = null;
                _linkedCarrierIds = [];
            }

            if (oldLoaded) {
                _safeExecutor.PublishEventAsync(LoadStatusChanged, this, new CarrierLoadStatusChangedEventArgs {
                    CarrierId = Id,
                    OldIsLoaded = true,
                    NewIsLoaded = false,
                    CurrentInductionCarrierId = null,
                    ChangedAt = DateTime.Now,
                }, "InfraredSensorCarrier.LoadStatusChanged");
            }

            return ValueTask.FromResult(true);
        }

        /// <summary>
        /// 释放资源。
        /// </summary>
        public void Dispose() {
            // 内存对象无非托管资源。
        }
    }
}
