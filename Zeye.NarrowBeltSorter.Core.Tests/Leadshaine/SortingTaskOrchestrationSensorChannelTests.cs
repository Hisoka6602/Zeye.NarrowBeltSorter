using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Events.Io;
using Zeye.NarrowBeltSorter.Core.Options.Sorting;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine {
    /// <summary>
    /// 分拣编排服务传感器事件通道行为测试（Phase 3.2 顺序稳定化）。
    /// 验证：FIFO 写入顺序、通道关闭时不递增丢弃计数、通道满载时递增丢弃计数。
    /// </summary>
    public sealed class SortingTaskOrchestrationSensorChannelTests {

        /// <summary>
        /// 正常写入时应能从通道按 FIFO 顺序读取传感器事件，确保创建包裹与上车触发不会乱序。
        /// </summary>
        [Fact]
        public void OnSensorStateChanged_WhenChannelNotFull_ShouldWriteEventsInFifoOrder() {
            var service = SortingTaskOrchestrationReflectionTestHelper.CreateServiceForPrivateMethodTests(
                new SortingTaskTimingOptions());
            var createArgs = new SensorStateChangedEventArgs(
                1, "ParcelCreate", IoPointType.ParcelCreateSensor,
                IoState.Low, IoState.High, IoState.High, 100L);
            var triggerArgs = new SensorStateChangedEventArgs(
                2, "LoadingTrigger", IoPointType.LoadingTriggerSensor,
                IoState.Low, IoState.High, IoState.High, 200L);

            SortingTaskOrchestrationReflectionTestHelper.InvokeOnSensorStateChanged(service, createArgs);
            SortingTaskOrchestrationReflectionTestHelper.InvokeOnSensorStateChanged(service, triggerArgs);

            var channel = SortingTaskOrchestrationReflectionTestHelper.GetSensorEventChannel(service);
            Assert.True(channel.Reader.TryRead(out var first));
            Assert.True(channel.Reader.TryRead(out var second));
            Assert.Equal(createArgs.OccurredAtMs, first.OccurredAtMs);
            Assert.Equal(triggerArgs.OccurredAtMs, second.OccurredAtMs);
        }

        /// <summary>
        /// 通道已关闭时（服务正在停止）TryWrite 失败属正常关闭流程，不应递增丢弃计数。
        /// </summary>
        [Fact]
        public void OnSensorStateChanged_WhenChannelCompleted_ShouldNotIncrementDropCount() {
            var service = SortingTaskOrchestrationReflectionTestHelper.CreateServiceForPrivateMethodTests(
                new SortingTaskTimingOptions());
            var channel = SortingTaskOrchestrationReflectionTestHelper.GetSensorEventChannel(service);
            // 步骤：先设置关闭标志再完成通道，与生产代码关闭顺序一致。
            SortingTaskOrchestrationReflectionTestHelper.SetSensorEventChannelCompleted(service, true);
            channel.Writer.TryComplete();
            var args = new SensorStateChangedEventArgs(
                1, "ParcelCreate", IoPointType.ParcelCreateSensor,
                IoState.Low, IoState.High, IoState.High, 100L);

            SortingTaskOrchestrationReflectionTestHelper.InvokeOnSensorStateChanged(service, args);

            Assert.Equal(0L, SortingTaskOrchestrationReflectionTestHelper.GetDroppedSensorEventCount(service));
        }

        /// <summary>
        /// 通道 TryWrite 失败且未设置关闭标志时（满载路径），应递增丢弃计数，确保满载可观测。
        /// </summary>
        [Fact]
        public void OnSensorStateChanged_WhenTryWriteFailsWithoutCompletedFlag_ShouldIncrementDropCount() {
            var service = SortingTaskOrchestrationReflectionTestHelper.CreateServiceForPrivateMethodTests(
                new SortingTaskTimingOptions());
            var channel = SortingTaskOrchestrationReflectionTestHelper.GetSensorEventChannel(service);
            // 步骤1：完成通道写入（使 TryWrite 返回 false），但不设置关闭标志，走"满载"分支而非"关闭"分支。
            channel.Writer.TryComplete();
            var args = new SensorStateChangedEventArgs(
                1, "ParcelCreateForDropTest", IoPointType.ParcelCreateSensor,
                IoState.Low, IoState.High, IoState.High, 100L);

            SortingTaskOrchestrationReflectionTestHelper.InvokeOnSensorStateChanged(service, args);

            Assert.Equal(1L, SortingTaskOrchestrationReflectionTestHelper.GetDroppedSensorEventCount(service));
        }
    }
}
