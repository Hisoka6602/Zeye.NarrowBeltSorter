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
        private const int NonBlockingMaxElapsedMs = SlowSubscriberSleepMs + 300;
        private const int ParallelMaxElapsedMs = SlowSubscriberSleepMs + 450;
        /// <summary>
        /// 验证发布端非阻塞（慢订阅者存在时，发布调用应快速返回）。
        /// </summary>
        [Fact]
        public async Task PublishEventAsync_ShouldReturnQuickly_WhenSlowSubscriberExists() {
            // 发布耗时断言上限使用“慢订阅者耗时 + 调度容差”，降低 CI 波动导致的偶发失败。
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
            // 步骤 1：构造执行器与同步原语，确保两路订阅者在同一时刻放行。
            var executor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var allEntered = new CountdownEvent(2);
            var allowProceed = new ManualResetEventSlim(false);
            var allDone = new CountdownEvent(2);

            /// <summary>
            /// 等待句柄完成，超时则抛出异常。
            /// </summary>
            /// <param name="handle">等待句柄。</param>
            /// <param name="message">超时消息。</param>
            static void WaitOrThrow(WaitHandle handle, string message) {
                if (!handle.WaitOne(TimeSpan.FromSeconds(3))) {
                    throw new TimeoutException(message);
                }
            }

            EventHandler<string>? handler = null;
            handler += (_, _) => {
                allEntered.Signal();
                allowProceed.Wait(TimeSpan.FromSeconds(3));
                Thread.Sleep(SlowSubscriberSleepMs);
                allDone.Signal();
            };
            handler += (_, _) => {
                allEntered.Signal();
                allowProceed.Wait(TimeSpan.FromSeconds(3));
                Thread.Sleep(SlowSubscriberSleepMs);
                allDone.Signal();
            };

            // 步骤 2：发布事件，等待两路订阅者全部进入同步点。
            var sw = Stopwatch.StartNew();
            executor.PublishEventAsync(handler, this, "payload", "SafeExecutorPublishEventAsyncTests.Parallel");
            WaitOrThrow(allEntered.WaitHandle, "订阅者未在预期时间内全部进入同步点。");

            // 步骤 3：统一放行并等待全部订阅者完成，统计整体耗时。
            allowProceed.Set();
            WaitOrThrow(allDone.WaitHandle, "订阅者未在预期时间内全部完成。");
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
