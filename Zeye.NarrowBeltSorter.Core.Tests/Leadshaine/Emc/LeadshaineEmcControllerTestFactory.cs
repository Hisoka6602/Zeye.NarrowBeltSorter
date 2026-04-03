using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc;
using DriverPointBindingOptions = Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine.LeadshaineIoPointBindingOption;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Emc {
    /// <summary>
    /// Leadshaine EMC 控制器测试工厂。
    /// </summary>
    public static class LeadshaineEmcControllerTestFactory {
        private const ushort DefaultCardNo = 0;
        private const ushort DefaultPortNo = 0;
        private const ushort DefaultErrorChannel = 0;
        private const int DefaultInputBitIndex = 1;
        private const int DefaultOutputBitIndex = 3;

        /// <summary>
        /// 创建控制器并返回对应硬件适配器测试桩。
        /// </summary>
        /// <param name="includeOutputPoint">是否包含输出点位。</param>
        /// <param name="initializeRetryCount">初始化重试次数。</param>
        /// <param name="initializeRetryDelayMs">初始化重试间隔。</param>
        /// <param name="reconnectBaseDelayMs">重连基础间隔。</param>
        /// <param name="reconnectMaxDelayMs">重连最大间隔。</param>
        /// <returns>控制器与测试桩。</returns>
        public static (LeadshaineEmcController Controller, FakeLeadshaineEmcHardwareAdapter Adapter) CreateWithAdapter(
            bool includeOutputPoint = true,
            int initializeRetryCount = 1,
            int initializeRetryDelayMs = 10,
            int reconnectBaseDelayMs = 10,
            int reconnectMaxDelayMs = 20) {
            var adapter = new FakeLeadshaineEmcHardwareAdapter();
            var controller = Create(
                adapter,
                includeOutputPoint,
                initializeRetryCount,
                initializeRetryDelayMs,
                reconnectBaseDelayMs,
                reconnectMaxDelayMs);
            return (controller, adapter);
        }

        /// <summary>
        /// 使用测试桩创建控制器实例。
        /// </summary>
        /// <param name="adapter">硬件适配器测试桩。</param>
        /// <param name="includeOutputPoint">是否包含输出点位。</param>
        /// <param name="initializeRetryCount">初始化重试次数。</param>
        /// <param name="initializeRetryDelayMs">初始化重试间隔。</param>
        /// <param name="reconnectBaseDelayMs">重连基础间隔。</param>
        /// <param name="reconnectMaxDelayMs">重连最大间隔。</param>
        /// <returns>控制器实例。</returns>
        public static LeadshaineEmcController Create(
            FakeLeadshaineEmcHardwareAdapter adapter,
            bool includeOutputPoint = true,
            int initializeRetryCount = 1,
            int initializeRetryDelayMs = 10,
            int reconnectBaseDelayMs = 10,
            int reconnectMaxDelayMs = 20) {
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            var connectionOptions = new LeadshaineEmcConnectionOptions {
                CardNo = DefaultCardNo,
                Channel = DefaultErrorChannel,
                ControllerIp = null,
                PollingIntervalMs = 50,
                InitializeRetryCount = initializeRetryCount,
                InitializeRetryDelayMs = initializeRetryDelayMs,
                ReconnectBaseDelayMs = reconnectBaseDelayMs,
                ReconnectMaxDelayMs = reconnectMaxDelayMs
            };
            var pointBindings = new LeadshaineIoPointBindingCollectionOptions {
                Points = BuildPointBindings(includeOutputPoint)
            };

            return new LeadshaineEmcController(safeExecutor, connectionOptions, pointBindings, adapter);
        }

        /// <summary>
        /// 创建 Leadshaine 物理位绑定配置。
        /// </summary>
        /// <param name="area">点位区域。</param>
        /// <param name="cardNo">板卡编号。</param>
        /// <param name="portNo">端口编号。</param>
        /// <param name="bitIndex">位索引。</param>
        /// <param name="triggerState">触发电平。</param>
        /// <returns>物理位绑定配置。</returns>
        public static LeadshaineBitBindingOption CreateBitBinding(
            string area,
            ushort cardNo,
            ushort portNo,
            int bitIndex,
            string triggerState = "High") {
            return new LeadshaineBitBindingOption {
                Area = area,
                CardNo = cardNo,
                PortNo = portNo,
                BitIndex = bitIndex,
                TriggerState = triggerState
            };
        }

        /// <summary>
        /// 构建测试点位绑定集合。
        /// </summary>
        /// <param name="includeOutputPoint">是否包含输出点位。</param>
        /// <returns>点位绑定集合。</returns>
        private static List<DriverPointBindingOptions> BuildPointBindings(bool includeOutputPoint) {
            var points = new List<DriverPointBindingOptions> {
                new() {
                    PointId = "I-01",
                    Binding = CreateBitBinding("Input", DefaultCardNo, DefaultPortNo, DefaultInputBitIndex)
                }
            };
            if (!includeOutputPoint) {
                return points;
            }

            points.Add(new DriverPointBindingOptions {
                PointId = "Q-01",
                Binding = CreateBitBinding("Output", DefaultCardNo, DefaultPortNo, DefaultOutputBitIndex)
            });
            return points;
        }
    }
}
