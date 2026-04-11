using System;
using System.Threading;
using System.Threading.Tasks;
using Zeye.NarrowBeltSorter.Core.Utilities;

namespace Zeye.NarrowBeltSorter.Core.Tests {

    /// <summary>
    /// <see cref="SortingChainLatencyStats"/> 单元测试：覆盖循环缓冲区覆盖、分桶映射、百分位边界与并发安全场景。
    /// </summary>
    public sealed class SortingChainLatencyStatsTests {

        /// <summary>
        /// 样本不足 2 个时 TryGetStats 应返回 false。
        /// </summary>
        [Theory]
        [InlineData("Low")]
        [InlineData("Medium")]
        [InlineData("High")]
        public async Task TryGetStats_WhenSampleCountLessThan2_ShouldReturnFalse(string bucket) {
            var stats = new SortingChainLatencyStats();
            stats.Record(100, bucket);

            var result = stats.TryGetStats(bucket, out _, out _, out _, out var count);

            Assert.False(result);
            Assert.Equal(1, count);
        }

        /// <summary>
        /// 恰好 2 个样本时 TryGetStats 应返回 true 并给出有效百分位。
        /// </summary>
        [Fact]
        public async Task TryGetStats_WithExactly2Samples_ShouldReturnTrue() {
            var stats = new SortingChainLatencyStats();
            stats.Record(100, "Low");
            stats.Record(200, "Low");

            var result = stats.TryGetStats("Low", out var p50, out _, out _, out _);

            Assert.True(result);
            Assert.Equal(150.0, p50, precision: 1);
        }

        /// <summary>
        /// P50 应为中位数；P95/P99 应趋近最大值（100 个升序样本）。
        /// </summary>
        [Fact]
        public async Task TryGetStats_With100AscendingSamples_ShouldComputeCorrectPercentiles() {
            var stats = new SortingChainLatencyStats();
            for (var i = 1; i <= 100; i++) {
                stats.Record(i, "Medium");
            }

            var result = stats.TryGetStats("Medium", out var p50, out var p95, out var p99, out var count);

            Assert.True(result);
            Assert.Equal(100, count);
            // P50 线性插值：rank = 0.5 * 99 = 49.5，lo=49, hi=50 → 50 + 0.5*(51-50) = 50.5
            Assert.Equal(50.5, p50, precision: 3);
            // P95 线性插值：rank = 0.95 * 99 = 94.05 → 95.05
            Assert.Equal(95.05, p95, precision: 3);
            // P99 线性插值：rank = 0.99 * 99 = 98.01 → 99.01
            Assert.Equal(99.01, p99, precision: 3);
        }

        /// <summary>
        /// 分桶映射：Low/Medium/High 应相互独立，互不干扰。
        /// </summary>
        [Fact]
        public async Task Record_DifferentBuckets_ShouldNotInterfereWithEachOther() {
            var stats = new SortingChainLatencyStats();
            stats.Record(10, "Low");
            stats.Record(10, "Low");
            stats.Record(500, "Medium");
            stats.Record(500, "Medium");
            stats.Record(9999, "High");
            stats.Record(9999, "High");

            Assert.True(stats.TryGetStats("Low", out var lowP50, out _, out _, out _));
            Assert.True(stats.TryGetStats("Medium", out var medP50, out _, out _, out _));
            Assert.True(stats.TryGetStats("High", out var highP50, out _, out _, out _));

            Assert.Equal(10.0, lowP50, precision: 1);
            Assert.Equal(500.0, medP50, precision: 1);
            Assert.Equal(9999.0, highP50, precision: 1);
        }

        /// <summary>
        /// 未识别分桶标签应归入 Medium 桶。
        /// </summary>
        [Fact]
        public async Task Record_UnknownBucketLabel_ShouldFallbackToMedium() {
            var stats = new SortingChainLatencyStats();
            stats.Record(777, "Unknown");
            stats.Record(777, "Unknown");

            Assert.True(stats.TryGetStats("Medium", out var p50, out _, out _, out _));
            Assert.Equal(777.0, p50, precision: 1);
        }

        /// <summary>
        /// 循环缓冲区：写入超过容量后旧样本应被覆盖，有效样本数应维持在容量上限。
        /// </summary>
        [Fact]
        public async Task Record_WhenExceedsCapacity_ShouldOverwriteOldestSamples() {
            var stats = new SortingChainLatencyStats();
            var capacity = SortingChainLatencyStats.CapacityPerBucket;

            // 步骤1：先写入全部为 1ms 的旧样本填满缓冲区。
            for (var i = 0; i < capacity; i++) {
                stats.Record(1, "High");
            }

            // 步骤2：再写入 10 个大值（9999ms），覆盖最早的 10 个样本。
            for (var i = 0; i < 10; i++) {
                stats.Record(9999, "High");
            }

            Assert.True(stats.TryGetStats("High", out _, out _, out _, out var count));
            // 有效样本数应维持在容量上限。
            Assert.Equal(capacity, count);
        }

        /// <summary>
        /// 写指针在循环覆盖 CapacityPerBucket 次以上时不应发生越界（防止 int 溢出）。
        /// </summary>
        [Fact]
        public async Task Record_AfterManyWrites_ShouldNotThrowIndexOutOfRange() {
            var stats = new SortingChainLatencyStats();
            var capacity = SortingChainLatencyStats.CapacityPerBucket;

            // 写入 3 * capacity 轮，模拟长时间运行。
            for (var i = 0; i < capacity * 3; i++) {
                stats.Record(i % 200, "Low");
            }

            var exception = Record.Exception(() => {
                stats.TryGetStats("Low", out _, out _, out _, out _);
            });
            Assert.Null(exception);
        }

        /// <summary>
        /// TotalRecordCount 应准确反映所有分桶的累计写入总数。
        /// </summary>
        [Fact]
        public async Task TotalRecordCount_ShouldReflectAllBucketWrites() {
            var stats = new SortingChainLatencyStats();
            stats.Record(1, "Low");
            stats.Record(2, "Low");
            stats.Record(3, "Medium");
            stats.Record(4, "High");

            Assert.Equal(4L, stats.TotalRecordCount);
        }

        /// <summary>
        /// 并发写入时不应发生竞态崩溃，统计结果应可正常获取。
        /// </summary>
        [Fact]
        public async Task Record_ConcurrentWrites_ShouldNotCrash() {
            var stats = new SortingChainLatencyStats();
            var buckets = new[] { "Low", "Medium", "High" };
            var tasks = new Task[12];
            for (var t = 0; t < tasks.Length; t++) {
                var bucket = buckets[t % 3];
                tasks[t] = Task.Run(() => {
                    for (var i = 0; i < 200; i++) {
                        stats.Record(i, bucket);
                    }
                });
            }

            await Task.WhenAll(tasks);
            foreach (var bucket in buckets) {
                stats.TryGetStats(bucket, out _, out _, out _, out _);
            }
        }

        /// <summary>
        /// TryGetStats 与 Record 并发执行时不应崩溃。
        /// </summary>
        [Fact]
        public async Task TryGetStats_ConcurrentWithRecord_ShouldNotCrash() {
            var stats = new SortingChainLatencyStats();
            for (var i = 0; i < 100; i++) {
                stats.Record(i, "Medium");
            }

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var readTask = Task.Run(() => {
                while (!cts.Token.IsCancellationRequested) {
                    stats.TryGetStats("Medium", out _, out _, out _, out _);
                }
            });

            var writeTask = Task.Run(() => {
                for (var i = 0; i < 1000; i++) {
                    stats.Record(i % 500, "Medium");
                }
            });

            await Task.WhenAll(writeTask);
            cts.Cancel();
            await Task.WhenAll(readTask);
        }

        /// <summary>
        /// 单个样本时 P99 应等于该样本值（边界情况：样本数恰好为2时才可计算）。
        /// </summary>
        [Fact]
        public async Task TryGetStats_WithEqualSamples_ShouldReturnSameValueForAllPercentiles() {
            var stats = new SortingChainLatencyStats();
            for (var i = 0; i < 10; i++) {
                stats.Record(42, "Low");
            }

            Assert.True(stats.TryGetStats("Low", out var p50, out var p95, out var p99, out _));
            Assert.Equal(42.0, p50, precision: 3);
            Assert.Equal(42.0, p95, precision: 3);
            Assert.Equal(42.0, p99, precision: 3);
        }

        /// <summary>
        /// 总记录数为 0 时 TryGetExceedanceRate 应返回 false。
        /// </summary>
        [Theory]
        [InlineData("Low")]
        [InlineData("Medium")]
        [InlineData("High")]
        public async Task TryGetExceedanceRate_WhenNoRecords_ShouldReturnFalse(string bucket) {
            var stats = new SortingChainLatencyStats();

            var result = stats.TryGetExceedanceRate(bucket, out var errorRate, out var exceedanceCount, out var totalCount);

            Assert.False(result);
            Assert.Equal(0, errorRate);
            Assert.Equal(0L, exceedanceCount);
            Assert.Equal(0L, totalCount);
        }

        /// <summary>
        /// 有记录但无超阈值时 ErrorRate 应为 0。
        /// </summary>
        [Fact]
        public async Task TryGetExceedanceRate_WhenNoExceedances_ShouldReturnZeroRate() {
            var stats = new SortingChainLatencyStats();
            stats.Record(100, "Medium");
            stats.Record(200, "Medium");

            var result = stats.TryGetExceedanceRate("Medium", out var errorRate, out var exceedanceCount, out var totalCount);

            Assert.True(result);
            Assert.Equal(0.0, errorRate);
            Assert.Equal(0L, exceedanceCount);
            Assert.Equal(2L, totalCount);
        }

        /// <summary>
        /// 所有记录均超阈值时 ErrorRate 应为 1.0。
        /// </summary>
        [Fact]
        public async Task TryGetExceedanceRate_WhenAllExceed_ShouldReturnRateOne() {
            var stats = new SortingChainLatencyStats();
            stats.Record(500, "High");
            stats.Record(600, "High");
            stats.RecordExceedance("High");
            stats.RecordExceedance("High");

            var result = stats.TryGetExceedanceRate("High", out var errorRate, out var exceedanceCount, out var totalCount);

            Assert.True(result);
            Assert.Equal(1.0, errorRate, precision: 3);
            Assert.Equal(2L, exceedanceCount);
            Assert.Equal(2L, totalCount);
        }

        /// <summary>
        /// 部分记录超阈值时 ErrorRate 应精确反映比例。
        /// </summary>
        [Fact]
        public async Task TryGetExceedanceRate_WhenPartialExceedances_ShouldReturnCorrectRate() {
            var stats = new SortingChainLatencyStats();
            for (var i = 0; i < 10; i++) {
                stats.Record(i * 100, "Low");
            }

            stats.RecordExceedance("Low");
            stats.RecordExceedance("Low");
            stats.RecordExceedance("Low");

            var result = stats.TryGetExceedanceRate("Low", out var errorRate, out var exceedanceCount, out var totalCount);

            Assert.True(result);
            Assert.Equal(0.3, errorRate, precision: 3);
            Assert.Equal(3L, exceedanceCount);
            Assert.Equal(10L, totalCount);
        }

        /// <summary>
        /// 不同密度分桶的超阈值计数应相互独立。
        /// </summary>
        [Fact]
        public async Task RecordExceedance_DifferentBuckets_ShouldNotInterfereWithEachOther() {
            var stats = new SortingChainLatencyStats();
            stats.Record(1, "Low");
            stats.Record(1, "Medium");
            stats.Record(1, "High");
            stats.RecordExceedance("Low");

            stats.TryGetExceedanceRate("Low", out var lowRate, out _, out _);
            stats.TryGetExceedanceRate("Medium", out var medRate, out _, out _);
            stats.TryGetExceedanceRate("High", out var highRate, out _, out _);

            Assert.Equal(1.0, lowRate, precision: 3);
            Assert.Equal(0.0, medRate, precision: 3);
            Assert.Equal(0.0, highRate, precision: 3);
        }

        /// <summary>
        /// 未识别分桶的超阈值记录应归入 Medium 桶。
        /// </summary>
        [Fact]
        public async Task RecordExceedance_UnknownBucket_ShouldFallbackToMedium() {
            var stats = new SortingChainLatencyStats();
            stats.Record(1, "Unknown");
            stats.RecordExceedance("Unknown");

            var result = stats.TryGetExceedanceRate("Medium", out var errorRate, out var exceedanceCount, out var totalCount);

            Assert.True(result);
            Assert.Equal(1L, exceedanceCount);
            Assert.Equal(1L, totalCount);
            Assert.Equal(1.0, errorRate, precision: 3);
        }
    }
}
