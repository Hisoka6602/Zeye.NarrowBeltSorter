using Zeye.NarrowBeltSorter.Core.Algorithms;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa {

    /// <summary>
    /// 单从站轴运行状态快照。
    /// 保存各轴独立 PID 状态与实时测量数据，索引与 <c>slaveClients</c> 对齐。
    /// </summary>
    internal sealed class SlaveAxisState {

        /// <summary>
        /// 最近一次采样得到的实时速度（mm/s）。
        /// 未采样成功时保留上次有效值，供 PID 降级使用。
        /// </summary>
        public decimal LastSampledMmps { get; set; }

        /// <summary>
        /// 最近一次 PID 计算的速度误差（mm/s），即目标速度 − 实测速度。
        /// </summary>
        public decimal LastErrorMmps { get; set; }

        /// <summary>
        /// 最近一次向本轴下发的转矩原始值（P3.10 raw），用于去重节流，避免无变化重复写入。
        /// </summary>
        public ushort LastIssuedTorqueRaw { get; set; }

        /// <summary>
        /// 本轴独立 PID 控制器状态（积分累计值、误差历史、微分滤波历史）。
        /// </summary>
        public PidControllerState PidState { get; set; }

        /// <summary>
        /// 当前轮询轮次是否存在有效采样。
        /// 采样失败（Modbus 通信错误等）时置 false，PID 仍可使用 <see cref="LastSampledMmps"/> 降级。
        /// </summary>
        public bool HasValidSample { get; set; }
    }
}
