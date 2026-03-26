using Zeye.NarrowBeltSorter.Core.Enums.Carrier;
using Zeye.NarrowBeltSorter.Core.Enums.Device;
using Zeye.NarrowBeltSorter.Core.Events.Carrier;
using Zeye.NarrowBeltSorter.Core.Models.Parcel;

namespace Zeye.NarrowBeltSorter.Core.Manager.Carrier {

    /// <summary>
    /// 小车接口（描述单台小车状态与控制能力）
    /// </summary>
    public interface ICarrier : IDisposable {
        /// <summary>
        /// 小车 Id
        /// </summary>
        long Id { get; }

        /// <summary>
        /// 当前速度（单位：mm/s）
        /// </summary>
        decimal Speed { get; }

        /// <summary>
        /// 当前转向
        /// </summary>
        CarrierTurnDirection TurnDirection { get; }

        /// <summary>
        /// 当前连接状态
        /// </summary>
        DeviceConnectionStatus ConnectionStatus { get; }

        /// <summary>
        /// 当前是否载货
        /// </summary>
        bool IsLoaded { get; }

        /// <summary>
        /// 当前包裹（未载货时为 null）
        /// </summary>
        ParcelInfo? Parcel { get; }

        /// <summary>
        /// 并联小车 Id 集合（快照）
        /// </summary>
        IReadOnlyList<long> LinkedCarrierIds { get; }

        /// <summary>
        /// 是否被其他小车并联
        /// </summary>
        bool IsLinkedByOther { get; }

        /// <summary>
        /// 连接状态变更事件
        /// </summary>
        event EventHandler<CarrierConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

        /// <summary>
        /// 载货状态变更事件
        /// </summary>
        event EventHandler<CarrierLoadStatusChangedEventArgs>? LoadStatusChanged;

        /// <summary>
        /// 转向变更事件
        /// </summary>
        event EventHandler<CarrierTurnDirectionChangedEventArgs>? TurnDirectionChanged;

        /// <summary>
        /// 速度变更事件
        /// </summary>
        event EventHandler<CarrierSpeedChangedEventArgs>? SpeedChanged;

        /// <summary>
        /// 连接小车（连接失败或状态不允许连接时返回 false）
        /// </summary>
        ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 断开小车连接（断开失败或状态不允许断开时返回 false）
        /// </summary>
        ValueTask<bool> DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置转向（设置失败或状态不允许变更时返回 false）
        /// </summary>
        ValueTask<bool> SetTurnDirectionAsync(
            CarrierTurnDirection turnDirection,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置速度（设置失败或状态不允许变更时返回 false）
        /// </summary>
        ValueTask<bool> SetSpeedAsync(decimal speed, CancellationToken cancellationToken = default);

        /// <summary>
        /// 装载包裹（装载失败或状态不允许装载时返回 false）
        /// </summary>
        ValueTask<bool> LoadParcelAsync(
            ParcelInfo parcel,
            IReadOnlyList<long> linkedCarrierIds,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 卸载包裹（卸载失败或状态不允许卸载时返回 false）
        /// </summary>
        ValueTask<bool> UnloadParcelAsync(CancellationToken cancellationToken = default);
    }
}
