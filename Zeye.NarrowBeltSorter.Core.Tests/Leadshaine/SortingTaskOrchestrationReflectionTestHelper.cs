using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Options.Sorting;
using Zeye.NarrowBeltSorter.Execution.Services;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine {
    /// <summary>
    /// 分拣编排服务反射测试辅助工具。
    /// </summary>
    internal static class SortingTaskOrchestrationReflectionTestHelper {
        /// <summary>
        /// 创建用于私有方法反射测试的服务实例。
        /// </summary>
        /// <param name="options">时序配置。</param>
        /// <returns>服务实例。</returns>
        public static SortingTaskOrchestrationService CreateServiceForPrivateMethodTests(SortingTaskTimingOptions options) {
            var service = (SortingTaskOrchestrationService)RuntimeHelpers.GetUninitializedObject(typeof(SortingTaskOrchestrationService));
            SetPrivateField(service, "_sortingTaskTimingOptionsMonitor", OptionsMonitorTestHelper.Create(options));
            SetPrivateField(service, "_pendingLoadingTriggerOccurredAtQueue", new ConcurrentQueue<DateTime>());
            SetPrivateField(service, "_logger", NullLogger<SortingTaskOrchestrationService>.Instance);
            return service;
        }

        /// <summary>
        /// 设置待消费上车触发时间队列。
        /// </summary>
        /// <param name="service">服务实例。</param>
        /// <param name="timestamps">触发时间集合。</param>
        public static void SetLoadingTriggerQueue(
            SortingTaskOrchestrationService service,
            IReadOnlyCollection<DateTime> timestamps) {
            SetPrivateField(service, "_pendingLoadingTriggerOccurredAtQueue", new ConcurrentQueue<DateTime>(timestamps));
        }

        /// <summary>
        /// 调用私有方法 ResolveParcelMatureStartAt。
        /// </summary>
        /// <param name="service">服务实例。</param>
        /// <param name="parcelId">包裹编号。</param>
        /// <returns>成熟起始时间。</returns>
        public static DateTime InvokeResolveParcelMatureStartAt(SortingTaskOrchestrationService service, long parcelId) {
            var method = typeof(SortingTaskOrchestrationService).GetMethod(
                "ResolveParcelMatureStartAt",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (DateTime)method!.Invoke(service, [parcelId])!;
        }

        /// <summary>
        /// 调用私有方法 TryConsumeLoadingTriggerOccurredAt。
        /// </summary>
        /// <param name="service">服务实例。</param>
        /// <param name="parcelCreatedAt">包裹创建时间。</param>
        /// <returns>是否消费成功与消费值。</returns>
        public static (bool Consumed, DateTime ConsumedAt) InvokeTryConsumeLoadingTriggerOccurredAt(
            SortingTaskOrchestrationService service,
            DateTime parcelCreatedAt) {
            var method = typeof(SortingTaskOrchestrationService).GetMethod(
                "TryConsumeLoadingTriggerOccurredAt",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var args = new object?[] { parcelCreatedAt, null };
            var consumed = (bool)method!.Invoke(service, args)!;
            var consumedAt = args[1] is DateTime value ? value : default;
            return (consumed, consumedAt);
        }

        /// <summary>
        /// 设置私有字段值。
        /// </summary>
        /// <param name="target">目标对象。</param>
        /// <param name="fieldName">字段名。</param>
        /// <param name="value">字段值。</param>
        private static void SetPrivateField(object target, string fieldName, object value) {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field!.SetValue(target, value);
        }
    }
}
