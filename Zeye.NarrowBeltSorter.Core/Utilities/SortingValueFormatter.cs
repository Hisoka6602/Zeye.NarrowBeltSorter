using System;

namespace Zeye.NarrowBeltSorter.Core.Utilities {

    /// <summary>
    /// 分拣链路数值日志格式化工具。
    /// 统一"最多两位小数，整数不带小数位"的速度与延迟输出规范，禁止在调用方各处内联重复实现。
    /// </summary>
    public static class SortingValueFormatter {

        /// <summary>
        /// double 整数判定精度阈值（用于判断 double 值是否为整数）。
        /// </summary>
        private const double DoubleEpsilon = 1e-9;

        /// <summary>
        /// 将 <see cref="decimal"/> 速度值格式化为"最多两位小数，整数不带小数位"的字符串。
        /// </summary>
        /// <param name="value">速度值（mm/s）。</param>
        /// <returns>格式化字符串。</returns>
        public static string FormatSpeed(decimal value) {
            var truncated = decimal.Truncate(value);
            return value == truncated
                ? truncated.ToString("G")
                : Math.Round(value, 2).ToString("G");
        }

        /// <summary>
        /// 将可空 <see cref="decimal"/> 速度值格式化为字符串；不可用时返回 <c>"N/A"</c>。
        /// </summary>
        /// <param name="value">速度值（mm/s）；<c>null</c> 表示不可用。</param>
        /// <returns>格式化字符串或 <c>"N/A"</c>。</returns>
        public static string FormatSpeed(decimal? value) {
            return value.HasValue ? FormatSpeed(value.Value) : "N/A";
        }

        /// <summary>
        /// 将 <see cref="double"/> 值格式化为"最多两位小数，整数不带小数位"的字符串（延迟、周期等字段）。
        /// </summary>
        /// <param name="value">待格式化的值。</param>
        /// <returns>格式化字符串。</returns>
        public static string FormatDouble(double value) {
            var truncated = Math.Truncate(value);
            return Math.Abs(value - truncated) < DoubleEpsilon
                ? ((long)truncated).ToString()
                : Math.Round(value, 2).ToString("G");
        }
    }
}
