using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.ZhiQian {
    public sealed class ZhiQianClientAdapterFactory : IZhiQianClientAdapterFactory {
        public IZhiQianClientAdapter Create(ZhiQianDeviceOptions deviceOptions, ZhiQianChuteOptions sharedOptions) {
            return new ZhiQianBinaryClientAdapter(
                deviceOptions.Host,
                deviceOptions.Port,
                deviceOptions.DeviceAddress,
                sharedOptions.CommandTimeoutMs,
                sharedOptions.RetryCount,
                sharedOptions.RetryDelayMs);
        }
    }
}
