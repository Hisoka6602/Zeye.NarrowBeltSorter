namespace Zeye.NarrowBeltSorter.Core.Utilities.Chutes {
    /// <summary>
    /// 智嵌 DO 路号范围约束。
    /// </summary>
    public static class ZhiQianAddressMap {
        /// <summary>
        /// DO 路号最小值（含）
        /// </summary>
        public const int DoIndexMin = 1;

        /// <summary>
        /// DO 路号最大值（含）
        /// </summary>
        public const int DoIndexMax = 32;

        /// <summary>
        /// DO 通道总数
        /// </summary>
        public const int DoChannelCount = 32;

        /// <summary>
        /// 校验 DO 路号是否在合法范围内。
        /// </summary>
        /// <param name="doIndex">DO 路号。</param>
        /// <returns>合法返回 true，否则返回 false。</returns>
        public static bool ValidateDoIndex(int doIndex) => doIndex >= DoIndexMin && doIndex <= DoIndexMax;
    }
}
