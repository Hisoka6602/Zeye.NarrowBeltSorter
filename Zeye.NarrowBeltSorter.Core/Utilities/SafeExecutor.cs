using System;
using System.Threading.Tasks;
using System.Threading;
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
                var actionTask = action();
                await actionTask;
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

        /// <summary>
        /// 安全执行带取消令牌的异步方法。
        /// </summary>
        /// <param name="action">待执行操作。</param>
        /// <param name="operationName">操作名称。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="onException">异常回调。</param>
        /// <returns>执行是否成功。</returns>
        public async Task<bool> ExecuteAsync(
            Func<CancellationToken, ValueTask> action,
            string operationName,
            CancellationToken cancellationToken = default,
            Action<Exception>? onException = null) {
            try {
                var actionTask = action(cancellationToken);
                await actionTask.ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "执行操作失败: {OperationName}", operationName);
                onException?.Invoke(ex);
                return false;
            }
        }

        /// <summary>
        /// 安全执行带取消令牌与返回值的异步方法。
        /// </summary>
        /// <typeparam name="T">返回值类型。</typeparam>
        /// <param name="func">待执行方法。</param>
        /// <param name="operationName">操作名称。</param>
        /// <param name="fallback">失败时回退值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="onException">异常回调。</param>
        /// <returns>执行结果与返回值。</returns>
        public async Task<(bool Success, T Result)> ExecuteAsync<T>(
            Func<CancellationToken, ValueTask<T>> func,
            string operationName,
            T fallback,
            CancellationToken cancellationToken = default,
            Action<Exception>? onException = null) {
            try {
                var result = await func(cancellationToken).ConfigureAwait(false);
                return (true, result);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "执行操作失败: {OperationName}", operationName);
                onException?.Invoke(ex);
                return (false, fallback);
            }
        }

        /// <summary>
        /// 非阻塞并行发布事件：发布线程快速返回，每个订阅者独立执行互不影响。
        /// 订阅者异常由统一安全执行器记录日志，不会反向阻塞发布者或其他订阅者。
        /// </summary>
        /// <typeparam name="TEventArgs">事件载荷类型。</typeparam>
        /// <param name="handler">事件处理器。</param>
        /// <param name="sender">事件发布者。</param>
        /// <param name="args">事件载荷。</param>
        /// <param name="operationName">操作名称。</param>
        public void PublishEventAsync<TEventArgs>(
            EventHandler<TEventArgs>? handler,
            object sender,
            TEventArgs args,
            string operationName) {
            if (handler is null) {
                return;
            }

            var subscribers = handler.GetInvocationList();
            foreach (var subscriber in subscribers) {
                if (subscriber is not EventHandler<TEventArgs> typedSubscriber) {
                    continue;
                }

                ThreadPool.UnsafeQueueUserWorkItem(
                    static state => {
                        var dispatchContext = ((SafeExecutor Executor,
                                               EventHandler<TEventArgs> Handler,
                                               object Sender,
                                               TEventArgs Args,
                                               string OperationName))state!;
                        dispatchContext.Executor.Execute(
                            () => dispatchContext.Handler(dispatchContext.Sender, dispatchContext.Args),
                            $"{dispatchContext.OperationName}.{dispatchContext.Handler.Method.Name}");
                    },
                    (this, typedSubscriber, sender, args, operationName),
                    preferLocal: false);
            }
        }
    }
}
