using Zeye.NarrowBeltSorter.Core.Enums.Device;
using Zeye.NarrowBeltSorter.Core.Models.Sensor;
using Zeye.NarrowBeltSorter.Core.Enums.SignalTower;
using Zeye.NarrowBeltSorter.Core.Events.SignalTower;

namespace Zeye.NarrowBeltSorter.Core.Manager.SignalTower {

    /// <summary>
    /// 信号塔接口（描述单个信号塔状态与控制能力）
    /// </summary>
    public interface ISignalTower {

        /// <summary>
        /// 信号塔 Id
        /// </summary>
        long Id { get; }

        /// <summary>
        /// 信号塔名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 红灯 IO
        /// </summary>
        SensorInfo RedLightIo { get; }

        /// <summary>
        /// 绿灯 IO
        /// </summary>
        SensorInfo GreenLightIo { get; }

        /// <summary>
        /// 黄灯 IO
        /// </summary>
        SensorInfo YellowLightIo { get; }

        /// <summary>
        /// 蜂鸣器 IO
        /// </summary>
        SensorInfo BuzzerIo { get; }

        /// <summary>
        /// 当前三色灯状态
        /// </summary>
        SignalTowerLightStatus LightStatus { get; }

        /// <summary>
        /// 当前蜂鸣器状态
        /// </summary>
        BuzzerStatus BuzzerStatus { get; }

        /// <summary>
        /// 当前连接状态
        /// </summary>
        DeviceConnectionStatus ConnectionStatus { get; }

        /// <summary>
        /// 三色灯状态变更事件
        /// </summary>
        event EventHandler<SignalTowerLightStatusChangedEventArgs>? LightStatusChanged;

        /// <summary>
        /// 蜂鸣器状态变更事件
        /// </summary>
        event EventHandler<SignalTowerBuzzerStatusChangedEventArgs>? BuzzerStatusChanged;

        /// <summary>
        /// 连接状态变更事件
        /// </summary>
        event EventHandler<SignalTowerConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

        /// <summary>
        /// 设置三色灯状态（设置失败或状态不允许变更时返回 false）
        /// </summary>
        ValueTask<bool> SetLightStatusAsync(
            SignalTowerLightStatus lightStatus,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 设置蜂鸣器状态（设置失败或状态不允许变更时返回 false）
        /// </summary>
        ValueTask<bool> SetBuzzerStatusAsync(
            BuzzerStatus buzzerStatus,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 按固定参数设置三色灯闪烁（灯类型、持续时间、闪烁间隔、次数）。
        /// </summary>
        /// <param name="lightStatus">目标灯类型。</param>
        /// <param name="onDuration">单次亮灯持续时间。</param>
        /// <param name="offDuration">单次灭灯持续时间。</param>
        /// <param name="repeatCount">闪烁次数（大于 0）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>设置是否成功。</returns>
        ValueTask<bool> BlinkLightAsync(
            SignalTowerLightStatus lightStatus,
            TimeSpan onDuration,
            TimeSpan offDuration,
            int repeatCount,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 按闪烁片段序列设置三色灯闪烁（灯类型、[亮灯持续时间、灭灯持续时间]集合）。
        /// </summary>
        /// <param name="lightStatus">目标灯类型。</param>
        /// <param name="segments">闪烁片段集合，按顺序执行。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>设置是否成功。</returns>
        ValueTask<bool> BlinkLightAsync(
            SignalTowerLightStatus lightStatus,
            IReadOnlyList<(TimeSpan OnDuration, TimeSpan OffDuration)> segments,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 按固定参数设置蜂鸣器闪鸣（持续时间、闪鸣间隔、次数）。
        /// </summary>
        /// <param name="onDuration">单次鸣响持续时间。</param>
        /// <param name="offDuration">单次静默持续时间。</param>
        /// <param name="repeatCount">闪鸣次数（大于 0）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>设置是否成功。</returns>
        ValueTask<bool> BlinkBuzzerAsync(
            TimeSpan onDuration,
            TimeSpan offDuration,
            int repeatCount,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 按闪鸣片段序列设置蜂鸣器闪鸣（[鸣响持续时间、静默持续时间]集合）。
        /// </summary>
        /// <param name="segments">闪鸣片段集合，按顺序执行。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>设置是否成功。</returns>
        ValueTask<bool> BlinkBuzzerAsync(
            IReadOnlyList<(TimeSpan OnDuration, TimeSpan OffDuration)> segments,
            CancellationToken cancellationToken = default);
    }
}
