using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Options.Sorting;
using Zeye.NarrowBeltSorter.Execution.Services;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.Io;
using Zeye.NarrowBeltSorter.Core.Events.System;
using Zeye.NarrowBeltSorter.Core.Manager.System;

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
            var carrierLoadingService = CreateCarrierLoadingServiceForPrivateMethodTests();
            SetPrivateField(service, "_sortingTaskTimingOptionsMonitor", OptionsMonitorTestHelper.Create(options));
            SetPrivateField(service, "_pendingLoadingTriggerParcelIdQueue", new ConcurrentQueue<long>());
            SetPrivateField(service, "_waitingLoadingTriggerParcelSet", new ConcurrentDictionary<long, byte>());
            SetPrivateField(service, "_parcelMatureStartAtMap", new ConcurrentDictionary<long, DateTime>());
            SetPrivateField(service, "_lostParcelIdSet", new ConcurrentDictionary<long, byte>());
            SetPrivateField(service, "_rawParcelQueue", new ConcurrentQueue<Core.Models.Parcel.ParcelInfo>());
            SetPrivateField(service, "_parcelSignal", new SemaphoreSlim(0));
            SetPrivateField(service, "_systemStateManager", new StubSystemStateManager(SystemState.Running));
            SetPrivateField(service, "_carrierLoadingService", carrierLoadingService);
            SetPrivateField(service, "_logger", NullLogger<SortingTaskOrchestrationService>.Instance);
            // 步骤：初始化传感器事件通道，供通道行为测试使用；字段初始化器在 GetUninitializedObject 时不会执行。
            SetPrivateField(service, "_sensorEventChannel", Channel.CreateBounded<SensorStateChangedEventArgs>(
                new BoundedChannelOptions(1024) {
                    FullMode = BoundedChannelFullMode.DropWrite,
                    SingleReader = true,
                    SingleWriter = false
                }));
            return service;
        }

        /// <summary>
        /// 设置待绑定包裹队列（FIFO）。
        /// </summary>
        public static void SetPendingLoadingTriggerParcelQueue(
            SortingTaskOrchestrationService service,
            IReadOnlyCollection<long> parcelIds) {
            SetPrivateField(service, "_pendingLoadingTriggerParcelIdQueue", new ConcurrentQueue<long>(parcelIds));
        }

        /// <summary>
        /// 设置等待上车触发绑定的包裹集合。
        /// </summary>
        public static void SetWaitingLoadingTriggerParcelSet(
            SortingTaskOrchestrationService service,
            IReadOnlyCollection<long> parcelIds) {
            var waitingSet = new ConcurrentDictionary<long, byte>();
            foreach (var parcelId in parcelIds) {
                waitingSet[parcelId] = 0;
            }

            SetPrivateField(service, "_waitingLoadingTriggerParcelSet", waitingSet);
        }

        /// <summary>
        /// 设置包裹成熟起始时间映射。
        /// </summary>
        public static void SetParcelMatureStartAtMap(
            SortingTaskOrchestrationService service,
            IReadOnlyDictionary<long, DateTime> map) {
            var matureStartMap = new ConcurrentDictionary<long, DateTime>(map);
            SetPrivateField(service, "_parcelMatureStartAtMap", matureStartMap);
        }

        /// <summary>
        /// 设置原始包裹队列。
        /// </summary>
        public static void SetRawParcelQueue(
            SortingTaskOrchestrationService service,
            IReadOnlyCollection<long> parcelIds) {
            var queue = new ConcurrentQueue<Core.Models.Parcel.ParcelInfo>();
            foreach (var parcelId in parcelIds) {
                queue.Enqueue(new Core.Models.Parcel.ParcelInfo { ParcelId = parcelId });
            }

            SetPrivateField(service, "_rawParcelQueue", queue);
        }

        /// <summary>
        /// 调用私有方法 TryGetOrCreateParcelMatureStartAt。
        /// </summary>
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
        /// 释放原始包裹信号量。
        /// </summary>
        public static void ReleaseParcelSignal(SortingTaskOrchestrationService service) {
            var field = typeof(SortingTaskOrchestrationService).GetField(
                "_parcelSignal",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var signal = (SemaphoreSlim)field!.GetValue(service)!;
            signal.Release();
        }

        /// <summary>
        /// 获取待绑定包裹队列长度。
        /// </summary>
        public static int GetPendingParcelQueueCount(SortingTaskOrchestrationService service) {
            var field = typeof(SortingTaskOrchestrationService).GetField(
                "_pendingLoadingTriggerParcelIdQueue",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var queue = (ConcurrentQueue<long>)field!.GetValue(service)!;
            return queue.Count;
        }

        /// <summary>
        /// 获取等待绑定包裹集合长度。
        /// </summary>
        public static int GetWaitingParcelSetCount(SortingTaskOrchestrationService service) {
            var field = typeof(SortingTaskOrchestrationService).GetField(
                "_waitingLoadingTriggerParcelSet",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var set = (ConcurrentDictionary<long, byte>)field!.GetValue(service)!;
            return set.Count;
        }

        /// <summary>
        /// 获取丢失包裹集合长度。
        /// </summary>
        public static int GetLostParcelSetCount(SortingTaskOrchestrationService service) {
            var field = typeof(SortingTaskOrchestrationService).GetField(
                "_lostParcelIdSet",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var set = (ConcurrentDictionary<long, byte>)field!.GetValue(service)!;
            return set.Count;
        }

        /// <summary>
        /// 获取成熟起始映射。
        /// </summary>
        public static IReadOnlyDictionary<long, DateTime> GetParcelMatureStartAtMap(SortingTaskOrchestrationService service) {
            var field = typeof(SortingTaskOrchestrationService).GetField(
                "_parcelMatureStartAtMap",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (ConcurrentDictionary<long, DateTime>)field!.GetValue(service)!;
        }

        /// <summary>
        /// 获取传感器事件有序通道。
        /// </summary>
        public static Channel<SensorStateChangedEventArgs> GetSensorEventChannel(SortingTaskOrchestrationService service) {
            var field = typeof(SortingTaskOrchestrationService).GetField(
                "_sensorEventChannel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (Channel<SensorStateChangedEventArgs>)field!.GetValue(service)!;
        }

        /// <summary>
        /// 设置传感器事件通道（供容量可控的通道满载测试使用）。
        /// </summary>
        public static void SetSensorEventChannel(SortingTaskOrchestrationService service, Channel<SensorStateChangedEventArgs> channel) {
            SetPrivateField(service, "_sensorEventChannel", channel);
        }

        /// <summary>
        /// 设置传感器事件通道关闭标志。
        /// </summary>
        public static void SetSensorEventChannelCompleted(SortingTaskOrchestrationService service, bool completed) {
            SetPrivateField(service, "_sensorEventChannelCompleted", completed);
        }

        /// <summary>
        /// 获取传感器事件通道累计丢弃事件数。
        /// </summary>
        public static long GetDroppedSensorEventCount(SortingTaskOrchestrationService service) {
            var field = typeof(SortingTaskOrchestrationService).GetField(
                "_droppedSensorEventCount",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (long)field!.GetValue(service)!;
        }

        /// <summary>
        /// 调用私有方法 OnSensorStateChanged。
        /// </summary>
        public static void InvokeOnSensorStateChanged(SortingTaskOrchestrationService service, SensorStateChangedEventArgs args) {
            var method = typeof(SortingTaskOrchestrationService).GetMethod(
                "OnSensorStateChanged",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(service, [null, args]);
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

        /// <summary>
        /// 创建用于私有方法测试的上车编排服务实例。
        /// </summary>
        private static SortingTaskCarrierLoadingService CreateCarrierLoadingServiceForPrivateMethodTests() {
            var service = (SortingTaskCarrierLoadingService)RuntimeHelpers.GetUninitializedObject(typeof(SortingTaskCarrierLoadingService));
            SetPrivateField(service, "_readyParcelQueue", new ConcurrentQueue<Core.Models.Parcel.ParcelInfo>());
            SetPrivateField(service, "_carrierParcelMap", new ConcurrentDictionary<long, long>());
            SetPrivateField(service, "_loadingTriggerBoundAtMap", new ConcurrentDictionary<long, DateTime>());
            SetPrivateField(service, "_loadedAtMap", new ConcurrentDictionary<long, DateTime>());
            SetPrivateField(service, "_arrivedTargetChuteAtMap", new ConcurrentDictionary<long, DateTime>());
            SetPrivateField(service, "_createdToLoadingTriggerStats", new Zeye.NarrowBeltSorter.Core.Utilities.SortingChainLatencyStats());
            SetPrivateField(service, "_triggerToLoadedStats", new Zeye.NarrowBeltSorter.Core.Utilities.SortingChainLatencyStats());
            SetPrivateField(service, "_loadedToArrivedStats", new Zeye.NarrowBeltSorter.Core.Utilities.SortingChainLatencyStats());
            SetPrivateField(service, "_arrivedToDroppedStats", new Zeye.NarrowBeltSorter.Core.Utilities.SortingChainLatencyStats());
            SetPrivateField(service, "_sortingTaskTimingOptionsMonitor", OptionsMonitorTestHelper.Create(new SortingTaskTimingOptions()));
            SetPrivateField(service, "_logger", NullLogger<SortingTaskCarrierLoadingService>.Instance);
            return service;
        }

    }
}
