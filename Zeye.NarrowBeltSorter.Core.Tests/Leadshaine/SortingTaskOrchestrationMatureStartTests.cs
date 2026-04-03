using System.Threading;
using System.Threading.Tasks;
using Zeye.NarrowBeltSorter.Core.Enums.Sorting;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Options.Sorting;
using Zeye.NarrowBeltSorter.Execution.Services;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine {
    /// <summary>
    /// 分拣编排成熟起始来源消费行为测试。
    /// </summary>
    public sealed class SortingTaskOrchestrationMatureStartTests {
        /// <summary>
        /// 上车触发应按包裹 FIFO 顺序绑定。
        /// </summary>
        [Fact]
        public void TryBindLoadingTriggerOccurredAt_ShouldBindParcelInFifoOrder() {
            var service = SortingTaskOrchestrationReflectionTestHelper.CreateServiceForPrivateMethodTests(new SortingTaskTimingOptions());
            var firstParcelId = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Local).Ticks;
            var secondParcelId = new DateTime(2025, 1, 1, 10, 0, 1, DateTimeKind.Local).Ticks;
            SortingTaskOrchestrationReflectionTestHelper.SetPendingLoadingTriggerParcelQueue(service, [firstParcelId, secondParcelId]);
            SortingTaskOrchestrationReflectionTestHelper.SetWaitingLoadingTriggerParcelSet(service, [firstParcelId, secondParcelId]);
            var firstTrigger = new DateTime(2025, 1, 1, 10, 0, 2, DateTimeKind.Local);
            var secondTrigger = firstTrigger.AddMilliseconds(50);

            var (firstBound, firstBoundParcelId) = SortingTaskOrchestrationReflectionTestHelper.InvokeTryBindLoadingTriggerOccurredAt(service, firstTrigger);
            var (secondBound, secondBoundParcelId) = SortingTaskOrchestrationReflectionTestHelper.InvokeTryBindLoadingTriggerOccurredAt(service, secondTrigger);
            var matureStartAtMap = SortingTaskOrchestrationReflectionTestHelper.GetParcelMatureStartAtMap(service);

            Assert.True(firstBound);
            Assert.True(secondBound);
            Assert.Equal(firstParcelId, firstBoundParcelId);
            Assert.Equal(secondParcelId, secondBoundParcelId);
            Assert.Equal(firstTrigger, matureStartAtMap[firstParcelId]);
            Assert.Equal(secondTrigger, matureStartAtMap[secondParcelId]);
        }

        /// <summary>
        /// 未启用回退时，未绑定上车触发的包裹应保持等待，不应提前成熟。
        /// </summary>
        [Fact]
        public void TryGetOrCreateParcelMatureStartAt_WhenWaitingLoadingTriggerAndFallbackDisabled_ShouldStayPending() {
            var options = new SortingTaskTimingOptions {
                ParcelMatureStartSource = ParcelMatureStartSource.LoadingTriggerSensor,
                EnableFallbackToParcelCreateWhenLoadingTriggerMissing = false
            };
            var service = SortingTaskOrchestrationReflectionTestHelper.CreateServiceForPrivateMethodTests(options);
            var parcelCreatedAt = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Local);
            var parcelId = parcelCreatedAt.Ticks;
            SortingTaskOrchestrationReflectionTestHelper.SetPendingLoadingTriggerParcelQueue(service, [parcelId]);
            SortingTaskOrchestrationReflectionTestHelper.SetWaitingLoadingTriggerParcelSet(service, [parcelId]);

            var (resolved, matureStartAt) = SortingTaskOrchestrationReflectionTestHelper.InvokeTryGetOrCreateParcelMatureStartAt(service, parcelId);
            var matureStartAtMap = SortingTaskOrchestrationReflectionTestHelper.GetParcelMatureStartAtMap(service);

            Assert.False(resolved);
            Assert.Equal(default, matureStartAt);
            Assert.False(matureStartAtMap.ContainsKey(parcelId));
        }

        /// <summary>
        /// 已绑定上车触发后应使用绑定时间作为成熟起始时间。
        /// </summary>
        [Fact]
        public void TryGetOrCreateParcelMatureStartAt_WhenLoadingTriggerBound_ShouldUseBoundTriggerTime() {
            var options = new SortingTaskTimingOptions {
                ParcelMatureStartSource = ParcelMatureStartSource.LoadingTriggerSensor,
                EnableFallbackToParcelCreateWhenLoadingTriggerMissing = false
            };
            var service = SortingTaskOrchestrationReflectionTestHelper.CreateServiceForPrivateMethodTests(options);
            var parcelCreatedAt = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Local);
            var parcelId = parcelCreatedAt.Ticks;
            var loadingTriggerAt = parcelCreatedAt.AddMilliseconds(500);
            SortingTaskOrchestrationReflectionTestHelper.SetParcelMatureStartAtMap(
                service,
                new Dictionary<long, DateTime> { [parcelId] = loadingTriggerAt });

            var (resolved, matureStartAt) = SortingTaskOrchestrationReflectionTestHelper.InvokeTryGetOrCreateParcelMatureStartAt(service, parcelId);

            Assert.True(resolved);
            Assert.Equal(loadingTriggerAt, matureStartAt);
        }

        /// <summary>
        /// 流水线模式下应以队头包裹作为成熟门控，队头未绑定时后续包裹不可推进。
        /// </summary>
        [Fact]
        public async Task WaitForPumpSignalAsync_WhenHeadParcelMissingTrigger_ShouldWaitForSignal() {
            var options = new SortingTaskTimingOptions {
                ParcelMatureStartSource = ParcelMatureStartSource.LoadingTriggerSensor,
                EnableFallbackToParcelCreateWhenLoadingTriggerMissing = false
            };
            var service = SortingTaskOrchestrationReflectionTestHelper.CreateServiceForPrivateMethodTests(options);
            var firstParcelId = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Local).Ticks;
            var secondParcelId = new DateTime(2025, 1, 1, 10, 0, 1, DateTimeKind.Local).Ticks;
            SortingTaskOrchestrationReflectionTestHelper.SetRawParcelQueue(service, [firstParcelId, secondParcelId]);
            SortingTaskOrchestrationReflectionTestHelper.SetPendingLoadingTriggerParcelQueue(service, [firstParcelId]);
            SortingTaskOrchestrationReflectionTestHelper.SetWaitingLoadingTriggerParcelSet(service, [firstParcelId]);
            SortingTaskOrchestrationReflectionTestHelper.SetParcelMatureStartAtMap(
                service,
                new Dictionary<long, DateTime> {
                    [secondParcelId] = new DateTime(2025, 1, 1, 10, 0, 2, DateTimeKind.Local)
                });

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var waitTask = SortingTaskOrchestrationReflectionTestHelper.InvokeWaitForPumpSignalAsync(service, CancellationToken.None);
            var timeoutTask = Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token);
            var completed = await Task.WhenAny(waitTask, timeoutTask);

            Assert.Same(timeoutTask, completed);
            SortingTaskOrchestrationReflectionTestHelper.ReleaseParcelSignal(service);
            await waitTask;
        }

        /// <summary>
        /// 上车触发滞后超窗时应判定包裹丢失并跳过上车。
        /// </summary>
        [Fact]
        public void TryBindLoadingTriggerOccurredAt_WhenLagExceedsWindow_ShouldMarkParcelLost() {
            var options = new SortingTaskTimingOptions {
                ParcelMatureStartSource = ParcelMatureStartSource.LoadingTriggerSensor,
                EnableFallbackToParcelCreateWhenLoadingTriggerMissing = false,
                LoadingTriggerLagWindowMs = 500
            };
            var service = SortingTaskOrchestrationReflectionTestHelper.CreateServiceForPrivateMethodTests(options);
            var parcelCreatedAt = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Local);
            var parcelId = parcelCreatedAt.Ticks;
            SortingTaskOrchestrationReflectionTestHelper.SetPendingLoadingTriggerParcelQueue(service, [parcelId]);
            SortingTaskOrchestrationReflectionTestHelper.SetWaitingLoadingTriggerParcelSet(service, [parcelId]);
            var lateTrigger = parcelCreatedAt.AddMilliseconds(1200);

            var (bound, boundParcelId) =
                SortingTaskOrchestrationReflectionTestHelper.InvokeTryBindLoadingTriggerOccurredAt(service, lateTrigger);

            Assert.False(bound);
            Assert.Equal(default, boundParcelId);
            Assert.Equal(1, SortingTaskOrchestrationReflectionTestHelper.GetLostParcelSetCount(service));
            Assert.False(SortingTaskOrchestrationReflectionTestHelper.GetParcelMatureStartAtMap(service).ContainsKey(parcelId));
        }

        /// <summary>
        /// 上车触发滞后超窗时应跳过丢失包裹，并继续尝试绑定后续可绑定包裹。
        /// </summary>
        [Fact]
        public void TryBindLoadingTriggerOccurredAt_WhenHeadLagExceedsWindow_ShouldContinueBindingNextParcel() {
            var options = new SortingTaskTimingOptions {
                ParcelMatureStartSource = ParcelMatureStartSource.LoadingTriggerSensor,
                EnableFallbackToParcelCreateWhenLoadingTriggerMissing = false,
                LoadingTriggerLagWindowMs = 500
            };
            var service = SortingTaskOrchestrationReflectionTestHelper.CreateServiceForPrivateMethodTests(options);
            var headParcelCreatedAt = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Local);
            var nextParcelCreatedAt = new DateTime(2025, 1, 1, 10, 0, 3, DateTimeKind.Local);
            var headParcelId = headParcelCreatedAt.Ticks;
            var nextParcelId = nextParcelCreatedAt.Ticks;
            SortingTaskOrchestrationReflectionTestHelper.SetPendingLoadingTriggerParcelQueue(service, [headParcelId, nextParcelId]);
            SortingTaskOrchestrationReflectionTestHelper.SetWaitingLoadingTriggerParcelSet(service, [headParcelId, nextParcelId]);
            var triggerAt = nextParcelCreatedAt.AddMilliseconds(100);

            var (bound, boundParcelId) =
                SortingTaskOrchestrationReflectionTestHelper.InvokeTryBindLoadingTriggerOccurredAt(service, triggerAt);

            Assert.True(bound);
            Assert.Equal(nextParcelId, boundParcelId);
            Assert.Equal(1, SortingTaskOrchestrationReflectionTestHelper.GetLostParcelSetCount(service));
            var matureStartAtMap = SortingTaskOrchestrationReflectionTestHelper.GetParcelMatureStartAtMap(service);
            Assert.Equal(triggerAt, matureStartAtMap[nextParcelId]);
            Assert.False(matureStartAtMap.ContainsKey(headParcelId));
        }

        /// <summary>
        /// 触发先到且暂无可绑定包裹时应直接丢弃，不进行缓存回放。
        /// </summary>
        [Fact]
        public void UpdateAndReplayLoadingTrigger_WhenTriggerArrivesBeforeParcel_ShouldDropTriggerWithoutCaching() {
            var options = new SortingTaskTimingOptions {
                ParcelMatureStartSource = ParcelMatureStartSource.LoadingTriggerSensor,
                EnableFallbackToParcelCreateWhenLoadingTriggerMissing = false
            };
            var service = SortingTaskOrchestrationReflectionTestHelper.CreateServiceForPrivateMethodTests(options);
            var triggerAt = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Local);

            SortingTaskOrchestrationReflectionTestHelper.InvokeUpdateLoadingTriggerOccurredAt(
                service,
                triggerAt.Ticks / TimeSpan.TicksPerMillisecond);

            Assert.Empty(SortingTaskOrchestrationReflectionTestHelper.GetParcelMatureStartAtMap(service));
        }

        /// <summary>
        /// 创建触发源模式应始终以包裹创建时间作为成熟起始，不受上车触发逻辑影响。
        /// </summary>
        [Fact]
        public void TryGetOrCreateParcelMatureStartAt_WhenParcelCreateSensorMode_ShouldUseCreateTime() {
            var options = new SortingTaskTimingOptions {
                ParcelMatureStartSource = ParcelMatureStartSource.ParcelCreateSensor
            };
            var service = SortingTaskOrchestrationReflectionTestHelper.CreateServiceForPrivateMethodTests(options);
            var parcelCreatedAt = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Local);
            var parcelId = parcelCreatedAt.Ticks;

            var (resolved, matureStartAt) =
                SortingTaskOrchestrationReflectionTestHelper.InvokeTryGetOrCreateParcelMatureStartAt(service, parcelId);

            Assert.True(resolved);
            Assert.Equal(parcelCreatedAt, matureStartAt);
        }

        /// <summary>
        /// 非运行态应清空运行期队列与映射。
        /// </summary>
        [Fact]
        public void ClearRuntimeQueuesForNonRunningState_ShouldClearRuntimeQueuesAndMaps() {
            var service = SortingTaskOrchestrationReflectionTestHelper.CreateServiceForPrivateMethodTests(
                new SortingTaskTimingOptions {
                    ParcelMatureStartSource = ParcelMatureStartSource.LoadingTriggerSensor
                });
            var parcelId = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Local).Ticks;
            SortingTaskOrchestrationReflectionTestHelper.SetPendingLoadingTriggerParcelQueue(service, [parcelId]);
            SortingTaskOrchestrationReflectionTestHelper.SetWaitingLoadingTriggerParcelSet(service, [parcelId]);
            SortingTaskOrchestrationReflectionTestHelper.SetParcelMatureStartAtMap(
                service,
                new Dictionary<long, DateTime> { [parcelId] = new DateTime(2025, 1, 1, 10, 0, 2, DateTimeKind.Local) });

            SortingTaskOrchestrationReflectionTestHelper.InvokeClearRuntimeQueuesForNonRunningState(service, SystemState.Paused);

            Assert.Equal(0, SortingTaskOrchestrationReflectionTestHelper.GetPendingParcelQueueCount(service));
            Assert.Equal(0, SortingTaskOrchestrationReflectionTestHelper.GetWaitingParcelSetCount(service));
            Assert.Empty(SortingTaskOrchestrationReflectionTestHelper.GetParcelMatureStartAtMap(service));
        }
    }
}
