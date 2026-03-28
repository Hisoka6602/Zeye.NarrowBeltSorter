namespace Zeye.NarrowBeltSorter.Core.Utilities.Chutes {

    /// <summary>
    /// 智嵌 32 路继电器静态地址工具。
    /// 统一管理 Y 路编号常量与有效性校验，避免重复代码。
    /// </summary>
    public static class ZhiQianAddressMap {

        /// <summary>
        /// Y 路编号起始值（Y 路从 1 开始）。
        /// </summary>
        public const int DoIndexMin = 1;

        /// <summary>
        /// Y 路编号最大值（最多 32 路）。
        /// </summary>
        public const int DoIndexMax = 32;

        /// <summary>
        /// DO 线圈总路数。
        /// </summary>
        public const int DoChannelCount = 32;

        /// <summary>
        /// 验证 Y 路编号是否在有效范围（1~32）内。
        /// </summary>
        /// <param name="doIndex">Y 路编号。</param>
        /// <returns>是否合法。</returns>
        public static bool IsValidDoIndex(int doIndex) => doIndex >= DoIndexMin && doIndex <= DoIndexMax;

        /// <summary>
        /// 校验 Y 路编号合法性，不合法时抛出 ArgumentOutOfRangeException。
        /// </summary>
        /// <param name="doIndex">Y 路编号（1~32）。</param>
        /// <exception cref="ArgumentOutOfRangeException">doIndex 不在 1~32 范围时抛出。</exception>
        public static void ValidateDoIndex(int doIndex) {
            if (!IsValidDoIndex(doIndex)) {
                throw new ArgumentOutOfRangeException(nameof(doIndex), $"DO 索引必须在 {DoIndexMin}~{DoIndexMax} 范围，当前值：{doIndex}。");
            }
        }
    }
}
