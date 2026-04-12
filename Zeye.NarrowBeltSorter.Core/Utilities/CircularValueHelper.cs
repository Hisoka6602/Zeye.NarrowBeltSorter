namespace Zeye.NarrowBeltSorter.Core.Utilities {

    /// <summary>
    /// 环形连续整数计算工具。
    /// 值域固定从 1 开始，到 totalCount 结束。
    /// </summary>
    public static class CircularValueHelper {

        /// <summary>
        /// 计算顺时针偏移后的值。
        /// 例如：当前值 100，总量 100，顺时针偏移 1，结果为 1。
        /// </summary>
        /// <param name="currentValue">当前值，范围必须为 1 到 totalCount。</param>
        /// <param name="offset">顺时针偏移量，必须大于等于 0。</param>
        /// <param name="totalCount">总量，必须大于 0。</param>
        /// <returns>顺时针偏移后的值。</returns>
        /// <exception cref="ArgumentOutOfRangeException">参数不合法时抛出。</exception>
        public static int GetClockwiseValue(int currentValue, int offset, int totalCount) {
            ValidateArguments(currentValue, offset, totalCount);

            return ((currentValue - 1 + offset) % totalCount) + 1;
        }

        /// <summary>
        /// 计算逆时针偏移后的值。
        /// 例如：当前值 1，总量 100，逆时针偏移 1，结果为 100。
        /// </summary>
        /// <param name="currentValue">当前值，范围必须为 1 到 totalCount。</param>
        /// <param name="offset">逆时针偏移量，必须大于等于 0。</param>
        /// <param name="totalCount">总量，必须大于 0。</param>
        /// <returns>逆时针偏移后的值。</returns>
        /// <exception cref="ArgumentOutOfRangeException">参数不合法时抛出。</exception>
        public static int GetCounterClockwiseValue(int currentValue, int offset, int totalCount) {
            ValidateArguments(currentValue, offset, totalCount);

            return ((currentValue - 1 - (offset % totalCount) + totalCount) % totalCount) + 1;
        }

        /// <summary>
        /// 校验参数是否合法。
        /// </summary>
        /// <param name="currentValue">当前值。</param>
        /// <param name="offset">偏移量。</param>
        /// <param name="totalCount">总量。</param>
        /// <exception cref="ArgumentOutOfRangeException">参数不合法时抛出。</exception>
        private static void ValidateArguments(int currentValue, int offset, int totalCount) {
            if (totalCount <= 0) {
                throw new ArgumentOutOfRangeException(nameof(totalCount), "总量必须大于 0。");
            }

            if (currentValue < 1 || currentValue > totalCount) {
                throw new ArgumentOutOfRangeException(nameof(currentValue), $"当前值必须在 1 到 {totalCount} 之间。");
            }

            if (offset < 0) {
                throw new ArgumentOutOfRangeException(nameof(offset), "偏移量不能小于 0。");
            }
        }

        /// <summary>
        /// 将任意整数索引（零基）映射到环形区间 [0, length) 内，支持负数索引。
        /// 例如：index=-1, length=5 → 4；index=5, length=5 → 0。
        /// </summary>
        /// <param name="index">原始索引，可为负数。</param>
        /// <param name="length">环形长度，必须大于 0；若为 0 则直接返回 0。</param>
        /// <returns>归一化后的索引，范围 [0, length)。</returns>
        public static int WrapIndex(int index, int length) {
            if (length <= 0) {
                return 0;
            }

            var result = index % length;
            return result < 0 ? result + length : result;
        }
    }
}
