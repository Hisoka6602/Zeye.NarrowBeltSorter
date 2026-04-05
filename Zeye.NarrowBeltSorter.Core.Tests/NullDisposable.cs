namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// 无操作空释放对象单例，用于接口要求返回 IDisposable 但无需实际资源管理的测试桩场景。
    /// </summary>
    internal sealed class NullDisposable : IDisposable {
        /// <summary>
        /// 获取唯一单例实例。
        /// </summary>
        public static readonly NullDisposable Instance = new();

        private NullDisposable() {
        }

        /// <summary>
        /// 无操作释放（测试桩不持有任何资源）。
        /// </summary>
        public void Dispose() {
        }
    }
}
