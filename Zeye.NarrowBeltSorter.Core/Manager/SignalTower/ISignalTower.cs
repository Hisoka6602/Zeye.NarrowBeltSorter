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

        //设置三色灯闪亮(灯类型、持续时间、闪烁间隔、次数)

        //设置三色灯闪亮(灯类型、、[持续时间、闪烁间隔]集合)

        //设置蜂鸣器闪鸣(持续时间、闪烁间隔、次数)

        //设置蜂鸣器闪鸣([持续时间、闪烁间隔]集合)
    }
}
