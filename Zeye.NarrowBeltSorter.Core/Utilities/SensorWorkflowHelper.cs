using Zeye.NarrowBeltSorter.Core.Manager.Emc;

namespace Zeye.NarrowBeltSorter.Core.Utilities {
    /// <summary>
    /// 传感器监控工作流通用辅助方法。
    /// </summary>
    public static class SensorWorkflowHelper {
        /// <summary>
        /// 同步传感器监控点位到 EMC 控制器（自动去重并过滤空值）。
        /// </summary>
        /// <param name="emcController">EMC 控制器。</param>
        /// <param name="pointIds">点位标识集合。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否同步成功。</returns>
        public static ValueTask<bool> SyncMonitoredIoPointsToEmcAsync(
            IEmcController emcController,
            IReadOnlyCollection<string> pointIds,
            CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(emcController);
            ArgumentNullException.ThrowIfNull(pointIds);

            var normalizedPointIds = pointIds
                .Where(static pointId => !string.IsNullOrWhiteSpace(pointId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return emcController.SetMonitoredIoPointsAsync(normalizedPointIds, cancellationToken);
        }

        /// <summary>
        /// 判断当前状态变化是否通过去抖窗口。
        /// </summary>
        /// <param name="currentTime">当前本地时间。</param>
        /// <param name="lastPublishedAt">上次发布该点位事件的时间。</param>
        /// <param name="debounceWindowMs">去抖窗口（毫秒）。</param>
        /// <returns>通过去抖返回 true，否则返回 false。</returns>
        public static bool PassDebounce(
            DateTime currentTime,
            DateTime? lastPublishedAt,
            int debounceWindowMs) {
            if (debounceWindowMs <= 0 || lastPublishedAt is null) {
                return true;
            }

            return (currentTime - lastPublishedAt.Value).TotalMilliseconds >= debounceWindowMs;
        }
    }
}
