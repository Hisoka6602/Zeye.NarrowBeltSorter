using System.Threading;

namespace Zeye.NarrowBeltSorter.Core.Utilities {

    /// <summary>
    /// <see cref="CancellationTokenSource"/> 线程安全操作工具，提供加锁取消与释放的统一实现。
    /// </summary>
    public static class CancellationTokenSourceHelper {

        /// <summary>
        /// 在持有 <paramref name="lockObj"/> 锁的前提下，将 <paramref name="field"/> 置为 <see langword="null"/>，
        /// 随后对原实例调用 <see cref="CancellationTokenSource.Cancel"/> 并 <see cref="CancellationTokenSource.Dispose"/>。
        /// 若字段已为 <see langword="null"/> 则静默返回。
        /// </summary>
        /// <param name="lockObj">用于保护字段的同步对象，不得为 <see langword="null"/>。</param>
        /// <param name="field">持有 <see cref="CancellationTokenSource"/> 的实例字段引用。</param>
        public static void CancelAndDispose(object lockObj, ref CancellationTokenSource? field) {
            CancellationTokenSource? cts;
            lock (lockObj) {
                cts = field;
                field = null;
            }

            if (cts is null) {
                return;
            }

            cts.Cancel();
            cts.Dispose();
        }
    }
}
