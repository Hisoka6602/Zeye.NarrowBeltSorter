using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.ZhiQian {
    /// <summary>
    /// 智嵌客户端适配器工厂实现。
    /// </summary>
    public sealed class ZhiQianClientAdapterFactory : IZhiQianClientAdapterFactory {
        /// <summary>
        /// 创建智嵌客户端适配器实例。
        /// </summary>
        /// <param name="deviceOptions">设备级配置。</param>
        /// <param name="sharedOptions">共享配置。</param>
        /// <returns>客户端适配器。</returns>
        public IZhiQianClientAdapter Create(ZhiQianDeviceOptions deviceOptions, ZhiQianChuteOptions sharedOptions) {
            return new ZhiQianBinaryClientAdapter(
                deviceOptions.Host,
                deviceOptions.Port,
                deviceOptions.DeviceAddress,
                sharedOptions.CommandTimeoutMs,
                sharedOptions.RetryCount,
                sharedOptions.RetryDelayMs,
                sharedOptions.CommandAbsoluteIntervalMs);
        }
    }
}
