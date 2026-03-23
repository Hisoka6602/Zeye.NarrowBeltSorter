using Microsoft.Extensions.Logging;

namespace Zeye.NarrowBeltSorter.Core.Utilities {

    /// <summary>
    /// 安全执行器 - 确保任何方法异常都不会导致程序崩溃
    /// </summary>
    public class SafeExecutor {

        /// <summary>
        /// 日志组件。
        /// </summary>
        private readonly ILogger<SafeExecutor> _logger;

        /// <summary>
        /// 初始化安全执行器。
        /// </summary>
        /// <param name="logger">日志组件。</param>
        public SafeExecutor(ILogger<SafeExecutor> logger) {
            _logger = logger;
        }

        /// <summary>
        /// 安全执行同步方法
        /// </summary>
        public bool Execute(Action action, string operationName) {
            try {
                action();
                return true;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "执行操作失败: {OperationName}", operationName);
                return false;
            }
        }

        /// <summary>
        /// 安全执行异步方法
        /// </summary>
        public async Task<bool> ExecuteAsync(Func<Task> action, string operationName) {
            try {
                await action();
                return true;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "执行操作失败: {OperationName}", operationName);
                return false;
            }
        }

        /// <summary>
        /// 安全执行带返回值的异步方法
        /// </summary>
        public async Task<(bool Success, T? Result)> ExecuteAsync<T>(Func<Task<T>> func, string operationName) {
            try {
                var result = await func();
                return (true, result);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "执行操作失败: {OperationName}", operationName);
                return (false, default);
            }
        }
    }
}
