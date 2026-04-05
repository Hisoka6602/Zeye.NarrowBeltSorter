using Microsoft.Extensions.Options;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// 选项监视器测试辅助工具。
    /// </summary>
    internal static class OptionsMonitorTestHelper {
        /// <summary>
        /// 将固定配置值包装为 IOptionsMonitor，供测试构造函数注入。
        /// </summary>
        /// <typeparam name="TOptions">选项类型。</typeparam>
        /// <param name="value">选项值。</param>
        /// <returns>固定值监视器。</returns>
        public static IOptionsMonitor<TOptions> Create<TOptions>(TOptions value) where TOptions : class {
            return new StaticOptionsMonitor<TOptions>(value);
        }
    }
}
