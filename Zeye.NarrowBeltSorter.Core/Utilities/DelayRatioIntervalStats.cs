using System.Threading;

namespace Zeye.NarrowBeltSorter.Core.Utilities {

    /// <summary>
    /// 上车匹配延迟占比区间分桶统计工具。
    /// 按固定区间（0~50%、50~80%、80~95%、95%+）累计计数，用于量化延迟占比分布。
    /// 所有计数操作均通过 <see cref="Interlocked"/> 保证线程安全，无锁高性能。
    /// </summary>
    public sealed class DelayRatioIntervalStats {

        /// <summary>
        /// 区间 [0%, 50%) 的累计计数。
        /// </summary>
        private long _bucket0To50;

        /// <summary>
        /// 区间 [50%, 80%) 的累计计数。
        /// </summary>
        private long _bucket50To80;

        /// <summary>
        /// 区间 [80%, 95%) 的累计计数。
        /// </summary>
        private long _bucket80To95;

        /// <summary>
        /// 区间 [95%, +∞) 的累计计数。
        /// </summary>
        private long _bucket95Plus;

        /// <summary>
        /// 记录一次延迟占比样本，将其归入对应区间并累加计数。
        /// </summary>
        /// <param name="delayRatioPercent">延迟占比（百分比，例如 95.5 表示 95.5%）。</param>
        public void Record(double delayRatioPercent) {
            if (delayRatioPercent < 50) {
                Interlocked.Increment(ref _bucket0To50);
            }
            else if (delayRatioPercent < 80) {
                Interlocked.Increment(ref _bucket50To80);
            }
            else if (delayRatioPercent < 95) {
                Interlocked.Increment(ref _bucket80To95);
            }
            else {
                Interlocked.Increment(ref _bucket95Plus);
            }
        }

        /// <summary>
        /// 读取各区间当前累计计数（使用 <see cref="Volatile.Read"/> 保证可见性）。
        /// </summary>
        /// <param name="count0To50">区间 [0%, 50%) 的累计次数。</param>
        /// <param name="count50To80">区间 [50%, 80%) 的累计次数。</param>
        /// <param name="count80To95">区间 [80%, 95%) 的累计次数。</param>
        /// <param name="count95Plus">区间 [95%, +∞) 的累计次数。</param>
        public void GetCounts(out long count0To50, out long count50To80, out long count80To95, out long count95Plus) {
            count0To50 = Volatile.Read(ref _bucket0To50);
            count50To80 = Volatile.Read(ref _bucket50To80);
            count80To95 = Volatile.Read(ref _bucket80To95);
            count95Plus = Volatile.Read(ref _bucket95Plus);
        }
    }
}
