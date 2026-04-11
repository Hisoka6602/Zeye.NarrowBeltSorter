using Zeye.NarrowBeltSorter.Execution.Services;
using System.Reflection;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.Io;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine {
    /// <summary>
    /// 分拣编排服务反射测试辅助工具——私有方法调用分部。
    /// </summary>
    internal static partial class SortingTaskOrchestrationReflectionTestHelper {
        /// <summary>
        /// 调用私有方法 TryGetOrCreateParcelMatureStartAt。
        /// </summary>
        /// <param name="service">分拣编排服务实例。</param>
        /// <param name="parcelId">目标包裹 ID。</param>
        /// <returns>解析结果与成熟起始时刻的元组。</returns>
        public static (bool Resolved, DateTime MatureStartAt) InvokeTryGetOrCreateParcelMatureStartAt(
            SortingTaskOrchestrationService service,
            long parcelId) {
            var method = typeof(SortingTaskOrchestrationService).GetMethod(
                "TryGetOrCreateParcelMatureStartAt",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var args = new object?[] { parcelId, null };
            var resolved = (bool)method!.Invoke(service, args)!;
            var matureStartAt = args[1] is DateTime value ? value : default;
            return (resolved, matureStartAt);
        }

        /// <summary>
        /// 调用私有方法 TryBindLoadingTriggerOccurredAt。
        /// </summary>
        /// <param name="service">分拣编排服务实例。</param>
        /// <param name="loadingTriggerOccurredAt">上车触发发生时刻。</param>
        /// <returns>绑定结果与包裹 ID 的元组。</returns>
        public static (bool Bound, long ParcelId) InvokeTryBindLoadingTriggerOccurredAt(
            SortingTaskOrchestrationService service,
            DateTime loadingTriggerOccurredAt) {
            var method = typeof(SortingTaskOrchestrationService).GetMethod(
                "TryBindLoadingTriggerOccurredAt",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var args = new object?[] { loadingTriggerOccurredAt, null };
            var bound = (bool)method!.Invoke(service, args)!;
            var parcelId = args[1] is long value ? value : default;
            return (bound, parcelId);
        }

        /// <summary>
        /// 调用私有方法 UpdateLoadingTriggerOccurredAt。
        /// </summary>
        /// <param name="service">分拣编排服务实例。</param>
        /// <param name="occurredAtMs">触发发生时刻（毫秒时间戳）。</param>
        public static void InvokeUpdateLoadingTriggerOccurredAt(
            SortingTaskOrchestrationService service,
            long occurredAtMs) {
            var method = typeof(SortingTaskOrchestrationService).GetMethod(
                "UpdateLoadingTriggerOccurredAt",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            _ = method!.Invoke(service, [occurredAtMs]);
        }

        /// <summary>
        /// 调用私有方法 ClearRuntimeQueuesForNonRunningState。
        /// </summary>
        /// <param name="service">分拣编排服务实例。</param>
        /// <param name="newState">切换后的系统状态。</param>
        public static void InvokeClearRuntimeQueuesForNonRunningState(
            SortingTaskOrchestrationService service,
            SystemState newState) {
            var method = typeof(SortingTaskOrchestrationService).GetMethod(
                "ClearRuntimeQueuesForNonRunningState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            _ = method!.Invoke(service, [newState]);
        }

        /// <summary>
        /// 调用私有方法 WaitForPumpSignalAsync。
        /// </summary>
        /// <param name="service">分拣编排服务实例。</param>
        /// <param name="stoppingToken">取消令牌。</param>
        /// <returns>等待信号量释放的异步任务。</returns>
        public static Task InvokeWaitForPumpSignalAsync(
            SortingTaskOrchestrationService service,
            CancellationToken stoppingToken) {
            var method = typeof(SortingTaskOrchestrationService).GetMethod(
                "WaitForPumpSignalAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (Task)method!.Invoke(service, [stoppingToken])!;
        }

        /// <summary>
        /// 调用私有方法 OnSensorStateChanged。
        /// </summary>
        /// <param name="service">分拣编排服务实例。</param>
        /// <param name="args">传感器状态变化事件参数。</param>
        public static void InvokeOnSensorStateChanged(SortingTaskOrchestrationService service, SensorStateChangedEventArgs args) {
            var method = typeof(SortingTaskOrchestrationService).GetMethod(
                "OnSensorStateChanged",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(service, [null, args]);
        }
    }
}
