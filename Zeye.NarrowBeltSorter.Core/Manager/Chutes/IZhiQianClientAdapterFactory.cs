using Zeye.NarrowBeltSorter.Core.Options.Chutes;

namespace Zeye.NarrowBeltSorter.Core.Manager.Chutes {
    /// <summary>
    /// 智嵌客户端适配器工厂接口，负责按配置创建适配器实例。
    /// </summary>
    public interface IZhiQianClientAdapterFactory {
        /// <summary>
        /// 根据设备配置与格口共享配置创建适配器实例。
        /// </summary>
        /// <param name="deviceOptions">设备连接配置。</param>
        /// <param name="sharedOptions">格口共享配置。</param>
        /// <returns>智嵌客户端适配器实例。</returns>
        IZhiQianClientAdapter Create(ZhiQianDeviceOptions deviceOptions, ZhiQianChuteOptions sharedOptions);
    }
}
