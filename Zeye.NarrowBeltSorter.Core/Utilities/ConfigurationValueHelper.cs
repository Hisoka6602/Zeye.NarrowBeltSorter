namespace Zeye.NarrowBeltSorter.Core.Utilities {

    /// <summary>
    /// 配置值安全回退工具。
    /// </summary>
    public static class ConfigurationValueHelper {
        /// <summary>
        /// 获取正整数配置值；当配置值小于等于零时回退默认值。
        /// </summary>
        /// <param name="configuredValue">配置值。</param>
        /// <param name="defaultValue">默认值。</param>
        /// <returns>可用于业务逻辑的安全值。</returns>
        public static int GetPositiveOrDefault(int configuredValue, int defaultValue) {
            return configuredValue > 0 ? configuredValue : defaultValue;
        }
    }
}
