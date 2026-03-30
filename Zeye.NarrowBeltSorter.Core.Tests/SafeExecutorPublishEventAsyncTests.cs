using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Utilities;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// SafeExecutor 并行事件分发测试。
    /// </summary>
    public sealed class SafeExecutorPublishEventAsyncTests {
        private const int SlowSubscriberSleepMs = 220;
        private const int NonBlockingMaxElapsedMs = 120;
        private const int ParallelMaxElapsedMs = 420;
        /// <summary>
        /// 验证发布端非阻塞（慢订阅者存在时，发布调用应快速返回）。
        /// </summary>
        [Fact]
        public async Task PublishEventAsync_ShouldReturnQuickly_WhenSlowSubscriberExists() {
            var executor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            EventHandler<string>? handler = null;
            handler += (_, _) => {
                Thread.Sleep(SlowSubscriberSleepMs);
                done.TrySetResult(true);
            };

            var sw = Stopwatch.StartNew();
            executor.PublishEventAsync(handler, this, "payload", "SafeExecutorPublishEventAsyncTests.NonBlocking");
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < NonBlockingMaxElapsedMs, $"发布调用耗时过长: {sw.ElapsedMilliseconds}ms");
            Assert.True(await done.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        }

        /// <summary>
        /// 验证订阅者并行执行（两个慢订阅者总耗时应接近单个耗时而非串行叠加）。
        /// </summary>
        [Fact]
        public async Task PublishEventAsync_ShouldRunSubscribersInParallel() {
            var executor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var counter = 0;
            var allDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnDone() {
                if (Interlocked.Increment(ref counter) == 2) {
                    allDone.TrySetResult(true);
                }
            }

            EventHandler<string>? handler = null;
            handler += (_, _) => {
                Thread.Sleep(SlowSubscriberSleepMs);
                OnDone();
            };
            handler += (_, _) => {
                Thread.Sleep(SlowSubscriberSleepMs);
                OnDone();
            };

            var sw = Stopwatch.StartNew();
            executor.PublishEventAsync(handler, this, "payload", "SafeExecutorPublishEventAsyncTests.Parallel");
            await allDone.Task.WaitAsync(TimeSpan.FromSeconds(3));
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < ParallelMaxElapsedMs, $"订阅者疑似串行执行: {sw.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// 验证异常订阅者隔离（一个订阅者异常不影响其他订阅者执行）。
        /// </summary>
        [Fact]
        public async Task PublishEventAsync_ShouldIsolateFaultedSubscriber() {
            var executor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var okDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            EventHandler<string>? handler = null;
            handler += (_, _) => throw new InvalidOperationException("boom");
            handler += (_, _) => okDone.TrySetResult(true);

            executor.PublishEventAsync(handler, this, "payload", "SafeExecutorPublishEventAsyncTests.Isolation");
            Assert.True(await okDone.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        }
    }
}
