using System;
using Zeye.NarrowBeltSorter.Core.Utilities;

namespace Zeye.NarrowBeltSorter.Core.Tests {

    /// <summary>
    /// <see cref="SensorTimeHelper"/> 单元测试：覆盖合法值解析、边界值、非法值（零/负/超上限）与 DateTimeKind 校验。
    /// </summary>
    public sealed class SensorTimeHelperTests {

        /// <summary>
        /// 典型正常值应解析成功并返回本地时间语义的 <see cref="DateTime"/>。
        /// </summary>
        [Fact]
        public void TryResolveLocalDateTime_WithValidValue_ShouldReturnTrueAndLocalKind() {
            var now = DateTime.Now;
            var occurredAtMs = now.Ticks / TimeSpan.TicksPerMillisecond;

            var result = SensorTimeHelper.TryResolveLocalDateTime(occurredAtMs, out var resolved);

            Assert.True(result);
            Assert.Equal(DateTimeKind.Local, resolved.Kind);
        }

        /// <summary>
        /// 正常值解析后的时间应与原始毫秒时间戳对应（精度为毫秒）。
        /// </summary>
        [Fact]
        public void TryResolveLocalDateTime_WithValidValue_ShouldMatchOriginalMillisecond() {
            var original = new DateTime(2024, 6, 15, 10, 30, 45, 123, DateTimeKind.Local);
            var occurredAtMs = original.Ticks / TimeSpan.TicksPerMillisecond;

            SensorTimeHelper.TryResolveLocalDateTime(occurredAtMs, out var resolved);

            // 仅比较毫秒精度（Ticks 取整导致 sub-ms 精度丢失）。
            Assert.Equal(original.Ticks / TimeSpan.TicksPerMillisecond, resolved.Ticks / TimeSpan.TicksPerMillisecond);
        }

        /// <summary>
        /// 零值应返回 false，resolved 应为 <see cref="DateTime.MinValue"/>。
        /// </summary>
        [Fact]
        public void TryResolveLocalDateTime_WithZero_ShouldReturnFalse() {
            var result = SensorTimeHelper.TryResolveLocalDateTime(0, out var resolved);

            Assert.False(result);
            Assert.Equal(DateTime.MinValue, resolved);
        }

        /// <summary>
        /// 负值应返回 false。
        /// </summary>
        [Theory]
        [InlineData(-1L)]
        [InlineData(long.MinValue)]
        public void TryResolveLocalDateTime_WithNegativeValue_ShouldReturnFalse(long occurredAtMs) {
            var result = SensorTimeHelper.TryResolveLocalDateTime(occurredAtMs, out _);

            Assert.False(result);
        }

        /// <summary>
        /// 超过 DateTime.MaxValue 对应毫秒数的值应返回 false。
        /// </summary>
        [Fact]
        public void TryResolveLocalDateTime_ExceedingMaxValue_ShouldReturnFalse() {
            var maxMs = DateTime.MaxValue.Ticks / TimeSpan.TicksPerMillisecond;

            var result = SensorTimeHelper.TryResolveLocalDateTime(maxMs + 1, out _);

            Assert.False(result);
        }

        /// <summary>
        /// 恰好等于 DateTime.MaxValue 对应毫秒数时应返回 true（合法边界）。
        /// </summary>
        [Fact]
        public void TryResolveLocalDateTime_AtMaxValueMs_ShouldReturnTrue() {
            var maxMs = DateTime.MaxValue.Ticks / TimeSpan.TicksPerMillisecond;

            var result = SensorTimeHelper.TryResolveLocalDateTime(maxMs, out var resolved);

            Assert.True(result);
            Assert.Equal(DateTimeKind.Local, resolved.Kind);
        }

        /// <summary>
        /// 值为 1（最小正合法值）时应解析成功。
        /// </summary>
        [Fact]
        public void TryResolveLocalDateTime_WithMinPositiveValue_ShouldReturnTrue() {
            var result = SensorTimeHelper.TryResolveLocalDateTime(1, out var resolved);

            Assert.True(result);
            Assert.Equal(DateTimeKind.Local, resolved.Kind);
        }
    }
}
