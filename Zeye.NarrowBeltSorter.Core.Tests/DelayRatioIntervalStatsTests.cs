using System.Threading;
using System.Threading.Tasks;
using Zeye.NarrowBeltSorter.Core.Utilities;

namespace Zeye.NarrowBeltSorter.Core.Tests {

    /// <summary>
    /// <see cref="DelayRatioIntervalStats"/> 单元测试：覆盖四档分桶边界值、负值、极大值与并发安全场景。
    /// </summary>
    public sealed class DelayRatioIntervalStatsTests {

        /// <summary>
        /// 零值（0%）应归入 0~50% 桶。
        /// </summary>
        [Fact]
        public void Record_Zero_ShouldFallInBucket0To50() {
            var stats = new DelayRatioIntervalStats();
            stats.Record(0);

            stats.GetCounts(out var c0, out var c50, out var c80, out var c95);

            Assert.Equal(1L, c0);
            Assert.Equal(0L, c50);
            Assert.Equal(0L, c80);
            Assert.Equal(0L, c95);
        }

        /// <summary>
        /// 负值应归入 0~50% 桶（无下界保护，负延迟占比视同低区间）。
        /// </summary>
        [Fact]
        public void Record_Negative_ShouldFallInBucket0To50() {
            var stats = new DelayRatioIntervalStats();
            stats.Record(-10);

            stats.GetCounts(out var c0, out _, out _, out _);

            Assert.Equal(1L, c0);
        }

        /// <summary>
        /// 49.999% 应归入 0~50% 桶（上界不含 50）。
        /// </summary>
        [Fact]
        public void Record_49Point999_ShouldFallInBucket0To50() {
            var stats = new DelayRatioIntervalStats();
            stats.Record(49.999);

            stats.GetCounts(out var c0, out var c50, out _, out _);

            Assert.Equal(1L, c0);
            Assert.Equal(0L, c50);
        }

        /// <summary>
        /// 恰好 50% 应归入 50~80% 桶（下界含 50）。
        /// </summary>
        [Fact]
        public void Record_Exactly50_ShouldFallInBucket50To80() {
            var stats = new DelayRatioIntervalStats();
            stats.Record(50);

            stats.GetCounts(out var c0, out var c50, out _, out _);

            Assert.Equal(0L, c0);
            Assert.Equal(1L, c50);
        }

        /// <summary>
        /// 79.999% 应归入 50~80% 桶（上界不含 80）。
        /// </summary>
        [Fact]
        public void Record_79Point999_ShouldFallInBucket50To80() {
            var stats = new DelayRatioIntervalStats();
            stats.Record(79.999);

            stats.GetCounts(out _, out var c50, out var c80, out _);

            Assert.Equal(1L, c50);
            Assert.Equal(0L, c80);
        }

        /// <summary>
        /// 恰好 80% 应归入 80~95% 桶（下界含 80）。
        /// </summary>
        [Fact]
        public void Record_Exactly80_ShouldFallInBucket80To95() {
            var stats = new DelayRatioIntervalStats();
            stats.Record(80);

            stats.GetCounts(out _, out var c50, out var c80, out _);

            Assert.Equal(0L, c50);
            Assert.Equal(1L, c80);
        }

        /// <summary>
        /// 94.999% 应归入 80~95% 桶（上界不含 95）。
        /// </summary>
        [Fact]
        public void Record_94Point999_ShouldFallInBucket80To95() {
            var stats = new DelayRatioIntervalStats();
            stats.Record(94.999);

            stats.GetCounts(out _, out _, out var c80, out var c95);

            Assert.Equal(1L, c80);
            Assert.Equal(0L, c95);
        }

        /// <summary>
        /// 恰好 95% 应归入 95%+ 桶（下界含 95）。
        /// </summary>
        [Fact]
        public void Record_Exactly95_ShouldFallInBucket95Plus() {
            var stats = new DelayRatioIntervalStats();
            stats.Record(95);

            stats.GetCounts(out _, out _, out var c80, out var c95);

            Assert.Equal(0L, c80);
            Assert.Equal(1L, c95);
        }

        /// <summary>
        /// 极大值（如 200%）应归入 95%+ 桶。
        /// </summary>
        [Fact]
        public void Record_VeryLargeValue_ShouldFallInBucket95Plus() {
            var stats = new DelayRatioIntervalStats();
            stats.Record(200);

            stats.GetCounts(out _, out _, out _, out var c95);

            Assert.Equal(1L, c95);
        }

        /// <summary>
        /// 多次 Record 后各桶计数应正确累加。
        /// </summary>
        [Fact]
        public void Record_MultipleValues_ShouldAccumulateCorrectly() {
            var stats = new DelayRatioIntervalStats();
            stats.Record(10);
            stats.Record(25);
            stats.Record(60);
            stats.Record(85);
            stats.Record(99);
            stats.Record(100);

            stats.GetCounts(out var c0, out var c50, out var c80, out var c95);

            Assert.Equal(2L, c0);
            Assert.Equal(1L, c50);
            Assert.Equal(1L, c80);
            Assert.Equal(2L, c95);
        }

        /// <summary>
        /// 初始状态所有桶计数应为 0。
        /// </summary>
        [Fact]
        public void GetCounts_InitialState_ShouldReturnAllZero() {
            var stats = new DelayRatioIntervalStats();

            stats.GetCounts(out var c0, out var c50, out var c80, out var c95);

            Assert.Equal(0L, c0);
            Assert.Equal(0L, c50);
            Assert.Equal(0L, c80);
            Assert.Equal(0L, c95);
        }

        /// <summary>
        /// 并发写入时不应发生竞态崩溃，各桶总计数应等于写入总次数。
        /// </summary>
        [Fact]
        public async Task Record_ConcurrentWrites_ShouldNotCrashAndCountsMatch() {
            var stats = new DelayRatioIntervalStats();
            const int taskCount = 8;
            const int writesPerTask = 100;

            var tasks = new Task[taskCount];
            for (var i = 0; i < taskCount; i++) {
                var offset = i * 10.0;
                tasks[i] = Task.Run(() => {
                    for (var j = 0; j < writesPerTask; j++) {
                        stats.Record(offset);
                    }
                });
            }

            await Task.WhenAll(tasks);

            stats.GetCounts(out var c0, out var c50, out var c80, out var c95);
            var total = c0 + c50 + c80 + c95;
            Assert.Equal((long)(taskCount * writesPerTask), total);
        }
    }
}
