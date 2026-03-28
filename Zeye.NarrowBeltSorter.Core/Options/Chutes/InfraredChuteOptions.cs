using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Zeye.NarrowBeltSorter.Core.Enums.Chutes;
using Zeye.NarrowBeltSorter.Core.Enums.Carrier;

namespace Zeye.NarrowBeltSorter.Core.Options.Chutes {

    /// <summary>
    /// 红外格口配置
    /// </summary>
    public class InfraredChuteOptions {

        /// <summary>
        /// 目标 DIN 通道编号
        /// 取值范围通常为 1~4，对应 DIN1~DIN4
        /// </summary>
        public required int DinChannel { get; init; }

        /// <summary>
        /// 默认运行方向
        /// </summary>
        public CarrierTurnDirection DefaultDirection { get; init; } = CarrierTurnDirection.Left;

        /// <summary>
        /// 控制模式
        /// 0=时间模式，1=位置模式
        /// </summary>
        public InfraredControlMode ControlMode { get; init; } = InfraredControlMode.Position;

        /// <summary>
        /// 默认速度，单位 mm/s
        /// </summary>
        public decimal DefaultSpeedMmps { get; init; }

        /// <summary>
        /// 默认运行时间，单位 ms
        /// 仅时间模式有效
        /// </summary>
        public int? DefaultDurationMs { get; init; }

        /// <summary>
        /// 默认运行距离，单位 mm
        /// 仅位置模式有效
        /// </summary>
        public decimal? DefaultDistanceMm { get; init; }

        /// <summary>
        /// 命中后保持时间，单位 ms
        /// 用于红外保持或脉冲保持一类场景
        /// </summary>
        public int HoldDurationMs { get; init; }

        /// <summary>
        /// 触发延迟时间，单位 ms
        /// </summary>
        public int TriggerDelayMs { get; init; }

        /// <summary>
        /// 滚筒直径，单位 mm，仅位置模式下用于计算转动距离与线性距离的换算
        /// </summary>
        public decimal RollerDiameterMm { get; init; }

        /// <summary>
        /// 拨码值
        /// </summary>
        public byte DialCode { get; init; }
    }
}
