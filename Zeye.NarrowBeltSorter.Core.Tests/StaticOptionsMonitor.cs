using Microsoft.Extensions.Options;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// 固定值选项监视器实现，用于测试中替代真实的 IOptionsMonitor。
    /// </summary>
    /// <typeparam name="TOptions">选项类型。</typeparam>
    internal sealed class StaticOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions> where TOptions : class {
        /// <summary>
        /// 获取当前配置值。
        /// </summary>
        public TOptions CurrentValue => value;

        /// <summary>
        /// 获取命名配置值。
        /// </summary>
        /// <param name="name">配置名称。</param>
        /// <returns>固定配置值。</returns>
        public TOptions Get(string? name) {
            return value;
        }

        /// <summary>
        /// 注册配置变更回调（固定值监视器不触发回调，返回无操作空释放对象）。
        /// </summary>
        /// <param name="listener">变更回调。</param>
        /// <returns>无操作的可释放对象。</returns>
        public IDisposable OnChange(Action<TOptions, string?> listener) {
            return NullDisposable.Instance;
        }
    }
}
