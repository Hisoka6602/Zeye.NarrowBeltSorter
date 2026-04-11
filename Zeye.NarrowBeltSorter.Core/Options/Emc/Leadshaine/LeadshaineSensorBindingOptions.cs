using Zeye.NarrowBeltSorter.Core.Enums.Io;

namespace Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine {
    /// <summary>
    /// Leadshaine 传感器点位绑定配置。
    /// </summary>
    public sealed record LeadshaineSensorBindingOptions : LeadshainePointReferenceOptions {
        /// <summary>
        /// 传感器名称。
        /// </summary>
        public string SensorName { get; set; } = string.Empty;

        /// <summary>
        /// 传感器类型（配置键：Type，优先于 SensorType；可选值：PanelButton、ParcelCreateSensor、FirstCarSensor、ChuteDropSensor、NonFirstCarSensor、AbnormalParcelBlockSensor、LoadingTriggerSensor）。
        /// </summary>
        public IoPointType? Type { get; set; }

        /// <summary>
        /// 传感器类型（默认：NonFirstCarSensor，作为 Type 未配置时的兼容回退；可选值：PanelButton、ParcelCreateSensor、FirstCarSensor、ChuteDropSensor、NonFirstCarSensor、AbnormalParcelBlockSensor、LoadingTriggerSensor）。
        /// </summary>
        public IoPointType SensorType { get; set; } = IoPointType.NonFirstCarSensor;

        /// <summary>
        /// 去抖窗口（单位：ms，最小值：0；0 表示不去抖）。
        /// </summary>
        public int DebounceWindowMs { get; set; }

        /// <summary>
        /// 轮询间隔（单位：ms，小于等于 0 时回退到 EmcConnection.PollingIntervalMs；建议范围：50~1000）。
        /// </summary>
        public int PollIntervalMs { get; set; }

        /// <summary>
        /// 解析当前传感器生效类型（优先使用 Type，未配置时回退到 SensorType）。
        /// </summary>
        /// <returns>生效的传感器类型。</returns>
        public IoPointType ResolveSensorType() {
            return Type ?? SensorType;
        }

        /// <summary>
        /// 解析当前传感器生效轮询间隔。
        /// </summary>
        /// <param name="defaultPollIntervalMs">默认轮询间隔。</param>
        /// <returns>生效轮询间隔（毫秒）。</returns>
        public int ResolvePollIntervalMs(int defaultPollIntervalMs) {
            return PollIntervalMs > 0 ? PollIntervalMs : defaultPollIntervalMs;
        }
    }
}
