using Microsoft.Extensions.Logging;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// 可捕获日志输出的测试 Logger。
    /// </summary>
    /// <typeparam name="T">日志分类类型。</typeparam>
    internal sealed class CapturingLogger<T> : ILogger<T> {
        private readonly List<string> _entries;

        /// <summary>
        /// 初始化捕获 Logger。
        /// </summary>
        /// <param name="entries">日志缓存集合。</param>
        public CapturingLogger(List<string> entries) {
            _entries = entries;
        }

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull {
            return null;
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) {
            return true;
        }

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) {
            var message = formatter(state, exception);
            var eventName = string.IsNullOrWhiteSpace(eventId.Name) ? "no-event" : eventId.Name;
            _entries.Add($"{logLevel}|{eventName}|{message}|{exception?.GetType().Name}|{exception?.Message}");
        }
    }
}
