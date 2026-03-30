using Zeye.NarrowBeltSorter.Core.Enums.Io;

namespace Zeye.NarrowBeltSorter.Core.Utilities {
    /// <summary>
    /// IO 绑定配置通用解析工具（跨厂商复用）。
    /// </summary>
    public static class IoBindingHelper {
        /// <summary>
        /// 解析触发电平配置字符串。
        /// </summary>
        /// <param name="triggerState">触发电平配置（"High" 或 "Low"，大小写不敏感；其他值默认返回 High）。</param>
        /// <returns>触发电平枚举值。</returns>
        public static IoState ParseTriggerState(string triggerState) {
            return string.Equals(triggerState, "Low", StringComparison.OrdinalIgnoreCase)
                ? IoState.Low
                : IoState.High;
        }
    }
}
