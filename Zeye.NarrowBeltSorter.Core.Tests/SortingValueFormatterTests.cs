using Zeye.NarrowBeltSorter.Core.Utilities;

namespace Zeye.NarrowBeltSorter.Core.Tests {

    /// <summary>
    /// <see cref="SortingValueFormatter"/> 单元测试：覆盖整数不带小数位、最多两位小数、四舍五入边界、
    /// decimal?/N/A 与 double 整数判定阈值行为。
    /// </summary>
    public sealed class SortingValueFormatterTests {

        // ── FormatSpeed(decimal) 重载测试 ───────────────────────────────────────

        /// <summary>
        /// 整数 decimal 不应输出小数位。
        /// </summary>
        [Theory]
        [InlineData(100, "100")]
        [InlineData(0, "0")]
        [InlineData(-5, "-5")]
        [InlineData(9999, "9999")]
        public void FormatSpeed_Decimal_Integer_ShouldHaveNoDecimalPoint(decimal value, string expected) {
            Assert.Equal(expected, SortingValueFormatter.FormatSpeed(value));
        }

        /// <summary>
        /// 带一位小数的值应原样输出（最多两位）。
        /// </summary>
        [Fact]
        public void FormatSpeed_Decimal_OneDecimalPlace_ShouldPreserveOnePlace() {
            Assert.Equal("123.4", SortingValueFormatter.FormatSpeed(123.4m));
        }

        /// <summary>
        /// 带两位小数的值应原样输出。
        /// </summary>
        [Fact]
        public void FormatSpeed_Decimal_TwoDecimalPlaces_ShouldPreserveTwoPlaces() {
            Assert.Equal("123.45", SortingValueFormatter.FormatSpeed(123.45m));
        }

        /// <summary>
        /// 三位及以上小数应四舍五入到两位。
        /// </summary>
        [Theory]
        [InlineData("123.456", "123.46")]
        [InlineData("123.454", "123.45")]
        [InlineData("1.235", "1.24")]
        public void FormatSpeed_Decimal_MoreThanTwoDecimals_ShouldRoundToTwo(string input, string expected) {
            Assert.Equal(expected, SortingValueFormatter.FormatSpeed(decimal.Parse(input)));
        }

        // ── FormatSpeed(decimal?) 重载测试 ──────────────────────────────────────

        /// <summary>
        /// null 值应返回 "N/A"。
        /// </summary>
        [Fact]
        public void FormatSpeed_NullableDecimal_Null_ShouldReturnNA() {
            Assert.Equal("N/A", SortingValueFormatter.FormatSpeed((decimal?)null));
        }

        /// <summary>
        /// 非 null 值应委托 FormatSpeed(decimal) 输出。
        /// </summary>
        [Fact]
        public void FormatSpeed_NullableDecimal_NonNull_ShouldDelegateToConcrete() {
            Assert.Equal("200", SortingValueFormatter.FormatSpeed((decimal?)200m));
            Assert.Equal("12.35", SortingValueFormatter.FormatSpeed((decimal?)12.354m));
        }

        // ── FormatDouble 重载测试 ────────────────────────────────────────────────

        /// <summary>
        /// 整数 double 不应输出小数位。
        /// </summary>
        [Theory]
        [InlineData(0.0, "0")]
        [InlineData(100.0, "100")]
        [InlineData(-50.0, "-50")]
        [InlineData(99999.0, "99999")]
        public void FormatDouble_Integer_ShouldHaveNoDecimalPoint(double value, string expected) {
            Assert.Equal(expected, SortingValueFormatter.FormatDouble(value));
        }

        /// <summary>
        /// 非整数 double 应输出最多两位小数。
        /// </summary>
        [Theory]
        [InlineData(1.5, "1.5")]
        [InlineData(1.23, "1.23")]
        public void FormatDouble_NonInteger_ShouldOutputUpToTwoDecimals(double value, string expected) {
            Assert.Equal(expected, SortingValueFormatter.FormatDouble(value));
        }

        /// <summary>
        /// 三位及以上小数应四舍五入到两位。
        /// </summary>
        [Theory]
        [InlineData(1.456, "1.46")]
        [InlineData(1.454, "1.45")]
        public void FormatDouble_MoreThanTwoDecimals_ShouldRoundToTwo(double value, string expected) {
            Assert.Equal(expected, SortingValueFormatter.FormatDouble(value));
        }

        /// <summary>
        /// 极小 sub-epsilon 偏差值（小于 1e-9）应被视为整数不输出小数位。
        /// </summary>
        [Fact]
        public void FormatDouble_SubEpsilonDeviation_ShouldTreatAsInteger() {
            // 1e-10 远小于 DoubleEpsilon（1e-9），应视为整数。
            var value = 42.0 + 1e-10;

            Assert.Equal("42", SortingValueFormatter.FormatDouble(value));
        }
    }
}
