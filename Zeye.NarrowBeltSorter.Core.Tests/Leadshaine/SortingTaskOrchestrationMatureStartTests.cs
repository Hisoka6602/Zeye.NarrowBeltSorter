using Zeye.NarrowBeltSorter.Core.Enums.Sorting;
using Zeye.NarrowBeltSorter.Core.Options.Sorting;
using Zeye.NarrowBeltSorter.Execution.Services;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine {
    /// <summary>
    /// 分拣编排成熟起始来源消费行为测试。
    /// </summary>
    public sealed class SortingTaskOrchestrationMatureStartTests {
        /// <summary>
        /// 上车触发时间应按包裹一对一消费。
        /// </summary>
        [Fact]
        public void TryConsumeLoadingTriggerOccurredAt_ShouldConsumeOneByOne() {
            var service = SortingTaskOrchestrationReflectionTestHelper.CreateServiceForPrivateMethodTests(
                new SortingTaskTimingOptions {
                    LoadingTriggerLeadWindowMs = 2000,
                    LoadingTriggerLagWindowMs = 5000
                });
            var parcelCreatedAt = DateTime.Now;
            SortingTaskOrchestrationReflectionTestHelper.SetLoadingTriggerQueue(
                service,
                [parcelCreatedAt.AddMilliseconds(-200)]);

            var (firstConsumed, _) = SortingTaskOrchestrationReflectionTestHelper.InvokeTryConsumeLoadingTriggerOccurredAt(service, parcelCreatedAt);
            var (secondConsumed, _) = SortingTaskOrchestrationReflectionTestHelper.InvokeTryConsumeLoadingTriggerOccurredAt(service, parcelCreatedAt);

            Assert.True(firstConsumed);
            Assert.False(secondConsumed);
        }

        /// <summary>
        /// 上车触发时间过晚时应判定为缺失并按配置回退。
        /// </summary>
        [Fact]
        public void ResolveParcelMatureStartAt_WhenLoadingTriggerTooLateAndFallbackEnabled_ShouldFallbackToCreateTime() {
            var options = new SortingTaskTimingOptions {
                ParcelMatureStartSource = ParcelMatureStartSource.LoadingTriggerSensor,
                EnableFallbackToParcelCreateWhenLoadingTriggerMissing = true,
                LoadingTriggerLeadWindowMs = 2000,
                LoadingTriggerLagWindowMs = 5000
            };
            var service = SortingTaskOrchestrationReflectionTestHelper.CreateServiceForPrivateMethodTests(options);
            var parcelCreatedAt = DateTime.Now;
            var parcelId = parcelCreatedAt.Ticks;
            SortingTaskOrchestrationReflectionTestHelper.SetLoadingTriggerQueue(
                service,
                [parcelCreatedAt.AddMilliseconds(7000)]);

            var matureStartAt = SortingTaskOrchestrationReflectionTestHelper.InvokeResolveParcelMatureStartAt(service, parcelId);

            Assert.Equal(new DateTime(parcelId, DateTimeKind.Local), matureStartAt);
        }

        /// <summary>
        /// 上车触发时间在窗口内时应被选为成熟起始时间。
        /// </summary>
        [Fact]
        public void ResolveParcelMatureStartAt_WhenLoadingTriggerWithinWindow_ShouldUseLoadingTriggerTime() {
            var options = new SortingTaskTimingOptions {
                ParcelMatureStartSource = ParcelMatureStartSource.LoadingTriggerSensor,
                EnableFallbackToParcelCreateWhenLoadingTriggerMissing = false,
                LoadingTriggerLeadWindowMs = 2000,
                LoadingTriggerLagWindowMs = 5000
            };
            var service = SortingTaskOrchestrationReflectionTestHelper.CreateServiceForPrivateMethodTests(options);
            var parcelCreatedAt = DateTime.Now;
            var parcelId = parcelCreatedAt.Ticks;
            var loadingTriggerAt = parcelCreatedAt.AddMilliseconds(-500);
            SortingTaskOrchestrationReflectionTestHelper.SetLoadingTriggerQueue(service, [loadingTriggerAt]);

            var matureStartAt = SortingTaskOrchestrationReflectionTestHelper.InvokeResolveParcelMatureStartAt(service, parcelId);

            Assert.Equal(loadingTriggerAt, matureStartAt);
        }
    }
}
