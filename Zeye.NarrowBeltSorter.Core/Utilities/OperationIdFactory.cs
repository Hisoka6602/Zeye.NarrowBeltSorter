namespace Zeye.NarrowBeltSorter.Core.Utilities {
    /// <summary>
    /// 操作编号工厂。
    /// </summary>
    public static class OperationIdFactory {
        /// <summary>
        /// 创建短格式操作编号（基于 GUID 的前 8 位十六进制字符）。
        /// </summary>
        /// <returns>用于日志关联的短操作编号。</returns>
        public static string CreateShortOperationId() {
            return Guid.NewGuid().ToString("N")[..8];
        }
    }
}
