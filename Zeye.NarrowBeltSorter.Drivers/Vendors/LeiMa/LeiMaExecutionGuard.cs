using Zeye.NarrowBeltSorter.Core.Events.Track;
using Zeye.NarrowBeltSorter.Core.Utilities;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa {
    /// <summary>
    /// 雷玛驱动危险调用隔离器。
    /// </summary>
    public sealed class LeiMaExecutionGuard {
        private readonly SafeExecutor _safeExecutor;
        private readonly Action<LoopTrackManagerFaultedEventArgs> _faultPublisher;

        /// <summary>
        /// 初始化危险调用隔离器。
        /// </summary>
        /// <param name="safeExecutor">统一安全执行器。</param>
        /// <param name="faultPublisher">故障事件发布委托。</param>
        public LeiMaExecutionGuard(
            SafeExecutor safeExecutor,
            Action<LoopTrackManagerFaultedEventArgs> faultPublisher) {
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _faultPublisher = faultPublisher ?? throw new ArgumentNullException(nameof(faultPublisher));
        }

        /// <summary>
        /// 安全执行异步操作并返回是否成功。
        /// </summary>
        /// <param name="operation">操作名称。</param>
        /// <param name="action">待执行操作。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>执行是否成功。</returns>
        public async ValueTask<bool> ExecuteAsync(
            string operation,
            Func<CancellationToken, ValueTask> action,
            CancellationToken cancellationToken = default) {
            try {
                await action(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) {
                await LogAndPublishFaultAsync(operation, ex).ConfigureAwait(false);
                return false;
            }
        }

        /// <summary>
        /// 安全执行异步操作并返回结果。
        /// </summary>
        /// <typeparam name="T">结果类型。</typeparam>
        /// <param name="operation">操作名称。</param>
        /// <param name="action">待执行操作。</param>
        /// <param name="fallback">失败时回退值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>执行成功标志与结果值。</returns>
        public async ValueTask<(bool Success, T Value)> ExecuteAsync<T>(
            string operation,
            Func<CancellationToken, ValueTask<T>> action,
            T fallback,
            CancellationToken cancellationToken = default) {
            try {
                var value = await action(cancellationToken).ConfigureAwait(false);
                return (true, value);
            }
            catch (Exception ex) {
                await LogAndPublishFaultAsync(operation, ex).ConfigureAwait(false);
                return (false, fallback);
            }
        }

        /// <summary>
        /// 记录故障日志并发布故障事件。
        /// </summary>
        /// <param name="operation">故障操作名称。</param>
        /// <param name="exception">异常对象。</param>
        private async Task LogAndPublishFaultAsync(string operation, Exception exception) {
            await _safeExecutor.ExecuteAsync(
                () => Task.FromException(exception),
                operation).ConfigureAwait(false);

            _faultPublisher.Invoke(new LoopTrackManagerFaultedEventArgs {
                Operation = operation,
                Exception = exception,
                FaultedAt = DateTime.Now
            });
        }
    }
}
