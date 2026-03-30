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

        public InfraredSensorCarrier(long id, SafeExecutor safeExecutor) {
            Id = id;
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
        }

        public long Id { get; }

        public decimal Speed { get; private set; }

        public CarrierTurnDirection TurnDirection { get; private set; } = CarrierTurnDirection.Left;

        public DeviceConnectionStatus ConnectionStatus { get; private set; } = DeviceConnectionStatus.Connected;

        public bool IsLoaded { get; private set; }

        public ParcelInfo? Parcel { get; private set; }

        public IReadOnlyList<long> LinkedCarrierIds { get; private set; } = [];

        public bool IsLinkedByOther => LinkedCarrierIds.Count > 0;

        public event EventHandler<CarrierConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

        public event EventHandler<CarrierLoadStatusChangedEventArgs>? LoadStatusChanged;

        public event EventHandler<CarrierTurnDirectionChangedEventArgs>? TurnDirectionChanged;

        public event EventHandler<CarrierSpeedChangedEventArgs>? SpeedChanged;

        public ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            var oldStatus = ConnectionStatus;
            ConnectionStatus = DeviceConnectionStatus.Connected;
            if (oldStatus != ConnectionStatus) {
                _safeExecutor.PublishEventAsync(ConnectionStatusChanged, this, new CarrierConnectionStatusChangedEventArgs {
                    CarrierId = Id,
                    OldStatus = oldStatus,
                    NewStatus = ConnectionStatus,
                    ChangedAt = DateTime.Now,
                }, "InfraredSensorCarrier.ConnectionStatusChanged");
            }

            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> DisconnectAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            var oldStatus = ConnectionStatus;
            ConnectionStatus = DeviceConnectionStatus.Disconnected;
            if (oldStatus != ConnectionStatus) {
                _safeExecutor.PublishEventAsync(ConnectionStatusChanged, this, new CarrierConnectionStatusChangedEventArgs {
                    CarrierId = Id,
                    OldStatus = oldStatus,
                    NewStatus = ConnectionStatus,
                    ChangedAt = DateTime.Now,
                }, "InfraredSensorCarrier.ConnectionStatusChanged");
            }

            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> SetTurnDirectionAsync(CarrierTurnDirection turnDirection, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            var oldDirection = TurnDirection;
            TurnDirection = turnDirection;
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

        public ValueTask<bool> SetSpeedAsync(decimal speedMmps, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            var oldSpeed = Speed;
            Speed = speedMmps;
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

        public ValueTask<bool> LoadParcelAsync(
            ParcelInfo parcel,
            IReadOnlyList<long> linkedCarrierIds,
            CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            Parcel = parcel;
            LinkedCarrierIds = linkedCarrierIds;
            var oldLoaded = IsLoaded;
            IsLoaded = true;
            if (oldLoaded != IsLoaded) {
                _safeExecutor.PublishEventAsync(LoadStatusChanged, this, new CarrierLoadStatusChangedEventArgs {
                    CarrierId = Id,
                    OldIsLoaded = oldLoaded,
                    NewIsLoaded = IsLoaded,
                    ChangedAt = DateTime.Now,
                }, "InfraredSensorCarrier.LoadStatusChanged");
            }

            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> UnloadParcelAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            Parcel = null;
            LinkedCarrierIds = [];
            var oldLoaded = IsLoaded;
            IsLoaded = false;
            if (oldLoaded != IsLoaded) {
                _safeExecutor.PublishEventAsync(LoadStatusChanged, this, new CarrierLoadStatusChangedEventArgs {
                    CarrierId = Id,
                    OldIsLoaded = oldLoaded,
                    NewIsLoaded = IsLoaded,
                    ChangedAt = DateTime.Now,
                }, "InfraredSensorCarrier.LoadStatusChanged");
            }

            return ValueTask.FromResult(true);
        }

        public void Dispose() {
            // 内存对象无非托管资源。
        }
    }
}
