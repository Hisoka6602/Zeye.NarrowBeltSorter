using Zeye.NarrowBeltSorter.Core.Utilities;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa {
    /// <summary>
    /// 雷码执行保护适配器（兼容层，底层统一复用 SafeExecutor）。
    /// </summary>
    public sealed class LeiMaExecutionGuard {
        private readonly SafeExecutor _safeExecutor;

        /// <summary>
        /// 初始化执行保护适配器。
        /// </summary>
        /// <param name="safeExecutor">统一安全执行器。</param>
        public LeiMaExecutionGuard(SafeExecutor safeExecutor) {
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
        }

        /// <summary>
        /// 安全执行异步操作并返回是否成功。
        /// </summary>
        /// <param name="operation">操作名称。</param>
        /// <param name="action">待执行操作。</param>
        /// <param name="onException">异常回调。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>执行是否成功。</returns>
        public Task<bool> ExecuteAsync(
            string operation,
            Func<CancellationToken, ValueTask> action,
            Action<Exception>? onException = null,
            CancellationToken cancellationToken = default) {
            // _logger.LogError 由 SafeExecutor.ExecuteAsync 内部统一处理。
            return _safeExecutor.ExecuteAsync(action, operation, cancellationToken, onException);
        }

        /// <summary>
        /// 安全执行异步操作并返回结果。
        /// </summary>
        /// <typeparam name="T">结果类型。</typeparam>
        /// <param name="operation">操作名称。</param>
        /// <param name="action">待执行操作。</param>
        /// <param name="fallback">失败时回退值。</param>
        /// <param name="onException">异常回调。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>执行成功标志与结果值。</returns>
        public Task<(bool Success, T Value)> ExecuteAsync<T>(
            string operation,
            Func<CancellationToken, ValueTask<T>> action,
            T fallback,
            Action<Exception>? onException = null,
            CancellationToken cancellationToken = default) {
            // _logger.LogError 由 SafeExecutor.ExecuteAsync 内部统一处理。
            return _safeExecutor.ExecuteAsync(action, operation, fallback, cancellationToken, onException);
        }
    }
}
