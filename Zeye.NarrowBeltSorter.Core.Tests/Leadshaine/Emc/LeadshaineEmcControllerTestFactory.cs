using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Options.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options;
using DriverPointBindingOptions = Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Options.LeadshainePointBindingOptions;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Emc {
    /// <summary>
    /// Leadshaine EMC 控制器测试工厂。
    /// </summary>
    public static class LeadshaineEmcControllerTestFactory {
        private const ushort DefaultCardNo = 0;
        private const ushort DefaultPortNo = 0;
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
                Channel = DefaultPortNo,
                ControllerIp = null,
                PollingIntervalMs = 50,
                InitializeRetryCount = initializeRetryCount,
                InitializeRetryDelayMs = initializeRetryDelayMs,
                ReconnectBaseDelayMs = reconnectBaseDelayMs,
                ReconnectMaxDelayMs = reconnectMaxDelayMs
            };
            var pointBindings = new LeadshainePointBindingCollectionOptions {
                Points = BuildPointBindings(includeOutputPoint)
            };

            return new LeadshaineEmcController(safeExecutor, connectionOptions, pointBindings, adapter);
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
                    Binding = new LeadshaineBitBindingOptions {
                        Area = "Input",
                        CardNo = DefaultCardNo,
                        PortNo = DefaultPortNo,
                        BitIndex = DefaultInputBitIndex,
                        TriggerState = "High"
                    }
                }
            };
            if (!includeOutputPoint) {
                return points;
            }

            points.Add(new DriverPointBindingOptions {
                PointId = "Q-01",
                Binding = new LeadshaineBitBindingOptions {
                    Area = "Output",
                    CardNo = DefaultCardNo,
                    PortNo = DefaultPortNo,
                    BitIndex = DefaultOutputBitIndex,
                    TriggerState = "High"
                }
            });
            return points;
        }
    }
}
