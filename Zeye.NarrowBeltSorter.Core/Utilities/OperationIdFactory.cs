namespace Zeye.NarrowBeltSorter.Core.Utilities {
    /// <summary>
    /// 操作编号工厂。
    /// </summary>
    public static class OperationIdFactory {
        /// <summary>
        /// 创建短格式操作编号。
        /// </summary>
        /// <returns>8位短编号。</returns>
        public static string CreateShortOperationId() {
            return Guid.NewGuid().ToString("N")[..8];
        }
    }
}
