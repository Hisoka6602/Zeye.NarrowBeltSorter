using System;

namespace Zeye.NarrowBeltSorter.Core.Utilities {

    /// <summary>
    /// 传感器触发时间解析工具。
    /// 统一处理 <c>OccurredAtMs</c>（.NET 本地时间 Ticks 毫秒值）到 <see cref="DateTime"/> 的转换，
    /// 避免在多处重复内联相同的合法性检查逻辑。
    /// </summary>
    public static class SensorTimeHelper {

        /// <summary>
        /// <c>OccurredAtMs</c> 合法范围上限（对应 <see cref="DateTime.MaxValue"/> 的毫秒时间戳）。
        /// </summary>
        private static readonly long MaxOccurredAtMs = DateTime.MaxValue.Ticks / TimeSpan.TicksPerMillisecond;

        /// <summary>
        /// 尝试将传感器事件的毫秒时间戳解析为本地时间语义的 <see cref="DateTime"/>。
        /// </summary>
        /// <param name="occurredAtMs">
        /// 传感器事件发生时间（以 .NET <see cref="DateTime"/> 基准 0001-01-01 为起点的本地时间毫秒时间戳）。
        /// 参数值应来源于本地时间 DateTime 的 Ticks 换算，例如：
        /// <c>DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond</c>（本地时间换算示例）。
        /// 禁止传入以 Unix Epoch 或其他相对时基计算的毫秒时间戳。
        /// </param>
        /// <param name="resolved">解析成功时的本地时间结果；失败时为 <see cref="DateTime.MinValue"/>。</param>
        /// <returns>解析成功返回 <c>true</c>；时间戳超出合法范围返回 <c>false</c>，调用方应回退 <see cref="DateTime.Now"/>。</returns>
        public static bool TryResolveLocalDateTime(long occurredAtMs, out DateTime resolved) {
            if (occurredAtMs > 0 && occurredAtMs <= MaxOccurredAtMs) {
                resolved = new DateTime(occurredAtMs * TimeSpan.TicksPerMillisecond, DateTimeKind.Local);
                return true;
            }

            resolved = DateTime.MinValue;
            return false;
        }
    }
}
