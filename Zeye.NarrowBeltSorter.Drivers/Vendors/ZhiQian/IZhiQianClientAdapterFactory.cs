using Zeye.NarrowBeltSorter.Core.Manager.Chutes;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.ZhiQian {
    public interface IZhiQianClientAdapterFactory {
        IZhiQianClientAdapter Create(ZhiQianDeviceOptions deviceOptions, ZhiQianChuteOptions sharedOptions);
    }
}
