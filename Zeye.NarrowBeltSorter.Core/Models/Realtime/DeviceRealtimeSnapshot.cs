using Zeye.NarrowBeltSorter.Core.Enums.Realtime;

namespace Zeye.NarrowBeltSorter.Core.Models.Realtime {
    /// <summary>
    /// 设备实时信息快照（可用于 SignalR/MQTT 下发）
    /// </summary>
    public sealed record class DeviceRealtimeSnapshot {
        /// <summary>
        /// 消息类型
        /// </summary>
        public required DeviceRealtimeMessageKind Kind { get; init; }

        /// <summary>
        /// 设备唯一标识（建议为稳定Id：例如 Track、Sensor、Chute、Carrier 等）
        /// </summary>
        public required string DeviceId { get; init; }

        /// <summary>
        /// 设备名称（可选）
        /// </summary>
        public string? DeviceName { get; init; }

        /// <summary>
        /// 设备子类型（可选，例如：Track、Camera、Reader、PLC、Drive）
        /// </summary>
        public string? DeviceType { get; init; }

        /// <summary>
        /// 发生时间
        /// </summary>
        public required DateTime Timestamp { get; init; }

        /// <summary>
        /// 数据载荷（建议为扁平 Key-Value，便于前端直接渲染）
        /// </summary>
        public required IReadOnlyDictionary<string, string> Metrics { get; init; }

        /// <summary>
        /// 是否为告警/异常状态
        /// </summary>
        public bool IsAlarm { get; init; }

        /// <summary>
        /// 告警码或错误码（可选）
        /// </summary>
        public string? AlarmCode { get; init; }

        /// <summary>
        /// 备注（可选）
        /// </summary>
        public string? Message { get; init; }
    }
}
