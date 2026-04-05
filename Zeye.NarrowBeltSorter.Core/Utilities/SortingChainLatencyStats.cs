using System;

namespace Zeye.NarrowBeltSorter.Core.Utilities {

    /// <summary>
    /// 分拣链路单阶段延迟滑动窗口统计工具（线程安全）。
    /// 按密度分桶（Low/Medium/High）分别维护循环缓冲区，支持 P50/P95/P99 百分位查询。
    /// 每个分桶最多保留 <see cref="CapacityPerBucket"/> 个最近样本，旧样本被循环覆盖。
    /// </summary>
    public sealed class SortingChainLatencyStats {

        /// <summary>
        /// 每个密度分桶保留的最大样本数。
        /// </summary>
        public const int CapacityPerBucket = 500;

        /// <summary>
        /// 密度分桶总数（Low=0 / Medium=1 / High=2）。
        /// </summary>
        private const int BucketCount = 3;

        /// <summary>
        /// Low 分桶索引。
        /// </summary>
        private const int LowIdx = 0;

        /// <summary>
        /// Medium 分桶索引。
        /// </summary>
        private const int MediumIdx = 1;

        /// <summary>
        /// High 分桶索引。
        /// </summary>
        private const int HighIdx = 2;

        /// <summary>
        /// 各分桶样本循环缓冲区（索引0=Low, 1=Medium, 2=High）。
        /// </summary>
        private readonly double[][] _samples;

        /// <summary>
        /// 各分桶下一次写入位置（始终保持在 [0, CapacityPerBucket) 范围内，避免 int 溢出导致取模为负）。
        /// </summary>
        private readonly int[] _writeIndices;

        /// <summary>
        /// 各分桶当前有效样本数（最大等于容量）。
        /// </summary>
        private readonly int[] _counts;

        /// <summary>
        /// 各分桶累计记录总数（不重置，用于外部判断是否需要输出统计日志）。
        /// </summary>
        private readonly long[] _totalRecordCounts;

        /// <summary>
        /// 各分桶超阈值累计次数（不重置，与 _totalRecordCounts 配合计算误差率）。
        /// </summary>
        private readonly long[] _exceedanceCounts;

        /// <summary>
        /// 并发保护锁。
        /// </summary>
        private readonly object _lock = new();

        /// <summary>
        /// 初始化 <see cref="SortingChainLatencyStats"/> 实例。
        /// </summary>
        public SortingChainLatencyStats() {
            _samples = new double[BucketCount][];
            _writeIndices = new int[BucketCount];
            _counts = new int[BucketCount];
            _totalRecordCounts = new long[BucketCount];
            _exceedanceCounts = new long[BucketCount];
            for (var i = 0; i < BucketCount; i++) {
                _samples[i] = new double[CapacityPerBucket];
            }
        }

        /// <summary>
        /// 所有分桶累计记录总数之和（用于触发周期性日志）。
        /// </summary>
        public long TotalRecordCount {
            get {
                lock (_lock) {
                    return _totalRecordCounts[LowIdx] + _totalRecordCounts[MediumIdx] + _totalRecordCounts[HighIdx];
                }
            }
        }

        /// <summary>
        /// 记录一个延迟样本到对应密度分桶。
        /// </summary>
        /// <param name="elapsedMs">链路耗时（毫秒）。</param>
        /// <param name="densityBucket">密度分桶标签（Low/Medium/High）。</param>
        public void Record(double elapsedMs, string densityBucket) {
            var idx = ResolveBucketIndex(densityBucket);
            lock (_lock) {
                // 步骤1：将样本写入当前写指针位置，写指针在容量范围内循环，避免 int 溢出后取模为负导致越界。
                var buf = _samples[idx];
                buf[_writeIndices[idx]] = elapsedMs;
                _writeIndices[idx] = (_writeIndices[idx] + 1) % CapacityPerBucket;
                if (_counts[idx] < CapacityPerBucket) {
                    _counts[idx]++;
                }

                // 步骤2：累计总记录数，供外部判断是否需要输出统计日志。
                _totalRecordCounts[idx]++;
            }
        }

        /// <summary>
        /// 尝试获取指定密度分桶的 P50/P95/P99 百分位统计数据。
        /// 样本数不足 2 个时返回 false。
        /// </summary>
        /// <param name="densityBucket">密度分桶标签（Low/Medium/High）。</param>
        /// <param name="p50">P50 百分位值（毫秒）。</param>
        /// <param name="p95">P95 百分位值（毫秒）。</param>
        /// <param name="p99">P99 百分位值（毫秒）。</param>
        /// <param name="sampleCount">当前有效样本数。</param>
        /// <returns>是否有足够样本。</returns>
        public bool TryGetStats(string densityBucket, out double p50, out double p95, out double p99, out int sampleCount) {
            var idx = ResolveBucketIndex(densityBucket);
            double[]? snapshot = null;
            lock (_lock) {
                sampleCount = _counts[idx];
                if (sampleCount < 2) {
                    p50 = p95 = p99 = 0;
                    return false;
                }

                // 步骤1：锁内仅拷贝样本快照，排序与百分位计算移到锁外，降低锁持有时间。
                snapshot = new double[sampleCount];
                Array.Copy(_samples[idx], snapshot, sampleCount);
            }

            // 步骤2：锁外排序与百分位计算，不阻塞热路径写入。
            Array.Sort(snapshot);
            p50 = ComputePercentile(snapshot, 50);
            p95 = ComputePercentile(snapshot, 95);
            p99 = ComputePercentile(snapshot, 99);
            return true;
        }

        /// <summary>
        /// 将密度分桶标签映射为内部索引（未识别标签默认归入 Medium 桶）。
        /// </summary>
        /// <param name="densityBucket">密度分桶标签。</param>
        /// <returns>分桶内部索引。</returns>
        private static int ResolveBucketIndex(string densityBucket) => densityBucket switch {
            "Low" => LowIdx,
            "High" => HighIdx,
            _ => MediumIdx
        };

        /// <summary>
        /// 记录一个超阈值样本到对应密度分桶的超阈值计数器。
        /// 应在链路耗时超过告警阈值时调用，与 <see cref="Record"/> 配对使用。
        /// </summary>
        /// <param name="densityBucket">密度分桶标签（Low/Medium/High）。</param>
        public void RecordExceedance(string densityBucket) {
            var idx = ResolveBucketIndex(densityBucket);
            lock (_lock) {
                _exceedanceCounts[idx]++;
            }
        }

        /// <summary>
        /// 尝试获取指定密度分桶的超阈值率（超阈值次数 / 总记录次数）。
        /// 总记录数为 0 时返回 false。
        /// </summary>
        /// <param name="densityBucket">密度分桶标签（Low/Medium/High）。</param>
        /// <param name="errorRate">超阈值率（0~1）。</param>
        /// <param name="exceedanceCount">超阈值累计次数。</param>
        /// <param name="totalCount">总记录累计次数。</param>
        /// <returns>是否有足够数据（总记录数 &gt; 0）。</returns>
        public bool TryGetExceedanceRate(string densityBucket, out double errorRate, out long exceedanceCount, out long totalCount) {
            var idx = ResolveBucketIndex(densityBucket);
            lock (_lock) {
                totalCount = _totalRecordCounts[idx];
                exceedanceCount = _exceedanceCounts[idx];
            }

            if (totalCount == 0) {
                errorRate = 0;
                return false;
            }

            errorRate = (double)exceedanceCount / totalCount;
            return true;
        }

        /// <summary>
        /// 线性插值法计算百分位值。
        /// </summary>
        /// <param name="sorted">已排序的样本数组。</param>
        /// <param name="pct">百分位（0~100）。</param>
        /// <returns>百分位对应值。</returns>
        private static double ComputePercentile(double[] sorted, int pct) {
            // 步骤1：计算浮点排名并确定上下边界索引。
            var rank = pct / 100.0 * (sorted.Length - 1);
            var lo = (int)Math.Floor(rank);
            var hi = lo + 1;
            // 步骤2：若超出范围则直接返回最大值；否则线性插值。
            if (hi >= sorted.Length) {
                return sorted[lo];
            }

            return sorted[lo] + (rank - lo) * (sorted[hi] - sorted[lo]);
        }
    }
}
