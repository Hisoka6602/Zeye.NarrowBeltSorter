using Microsoft.Extensions.Options;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// 选项监视器测试辅助工具。
    /// </summary>
    internal static class OptionsMonitorTestHelper {
        /// <summary>
        /// 空操作释放器，满足 OnChange 返回值契约。
        /// </summary>
        private sealed class NoopDisposable : IDisposable {
            /// <summary>
            /// 空操作释放。
            /// </summary>
            public void Dispose() {
            }
        }

        /// <summary>
        /// 将固定配置值包装为 IOptionsMonitor，供测试构造函数注入。
        /// </summary>
        /// <typeparam name="TOptions">选项类型。</typeparam>
        /// <param name="value">选项值。</param>
        /// <returns>固定值监视器。</returns>
        public static IOptionsMonitor<TOptions> Create<TOptions>(TOptions value) where TOptions : class {
            return new StaticOptionsMonitor<TOptions>(value);
        }

        /// <summary>
        /// 固定值选项监视器实现。
        /// </summary>
        /// <typeparam name="TOptions">选项类型。</typeparam>
        private sealed class StaticOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions> where TOptions : class {
            /// <summary>
            /// 共享空操作释放器实例。
            /// </summary>
            private static readonly NoopDisposable NoopChangeToken = new();

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
            /// 注册配置变更回调（固定值监视器不触发回调）。
            /// </summary>
            /// <param name="listener">变更回调。</param>
            /// <returns>空操作释放器。</returns>
            public IDisposable OnChange(Action<TOptions, string?> listener) {
                return NoopChangeToken;
            }
        }
    }
}
