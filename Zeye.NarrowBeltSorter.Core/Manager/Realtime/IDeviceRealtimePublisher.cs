using Zeye.NarrowBeltSorter.Core.Events.Realtime;
using Zeye.NarrowBeltSorter.Core.Models.Realtime;

namespace Zeye.NarrowBeltSorter.Core.Manager.Realtime {

    /// <summary>
    /// 设备实时信息发布器（面向前端/外部订阅端）
    /// <para>仅定义契约，不绑定 SignalR/MQTT 实现。</para>
    /// </summary>
    public interface IDeviceRealtimePublisher {

        /// <summary>
        /// 发布单条实时快照
        /// </summary>
        ValueTask PublishAsync(DeviceRealtimeSnapshot snapshot, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量发布实时快照（用于降低调用开销）
        /// </summary>
        ValueTask PublishBatchAsync(IReadOnlyList<DeviceRealtimeSnapshot> snapshots, CancellationToken cancellationToken = default);

        /// <summary>
        /// 发布器异常事件（用于隔离异常，不影响上层调用链）
        /// </summary>
        event EventHandler<DeviceRealtimePublisherFaultedEventArgs>? Faulted;
    }
}
