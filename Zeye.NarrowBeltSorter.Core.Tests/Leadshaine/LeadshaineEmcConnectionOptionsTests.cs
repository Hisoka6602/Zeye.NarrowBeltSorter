using Zeye.NarrowBeltSorter.Core.Options.Leadshaine;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine {
    /// <summary>
    /// Leadshaine EMC 连接参数校验测试。
    /// </summary>
    public sealed class LeadshaineEmcConnectionOptionsTests {
        /// <summary>
        /// 合法配置应返回空错误集合。
        /// </summary>
        [Fact]
        public void Validate_WhenOptionsAreValid_ShouldReturnNoErrors() {
            var options = CreateValidOptions();

            var errors = options.Validate();

            Assert.Empty(errors);
        }

        /// <summary>
        /// 非法边界值应返回对应错误。
        /// </summary>
        [Fact]
        public void Validate_WhenNumericBoundaryInvalid_ShouldReturnExpectedErrors() {
            var options = CreateValidOptions();
            options.ConnectionTimeoutMs = 0;
            options.InitializeRetryCount = -1;
            options.InitializeRetryDelayMs = 0;
            options.PollingIntervalMs = 0;
            options.ReconnectBaseDelayMs = 0;
            options.ReconnectMaxDelayMs = 0;

            var errors = options.Validate();

            Assert.Contains(errors, x => x.Contains("ConnectionTimeoutMs"));
            Assert.Contains(errors, x => x.Contains("InitializeRetryCount"));
            Assert.Contains(errors, x => x.Contains("InitializeRetryDelayMs"));
            Assert.Contains(errors, x => x.Contains("PollingIntervalMs"));
            Assert.Contains(errors, x => x.Contains("ReconnectBaseDelayMs"));
            Assert.Contains(errors, x => x.Contains("ReconnectMaxDelayMs"));
        }

        /// <summary>
        /// 重连最大间隔小于基础间隔时应报错。
        /// </summary>
        [Fact]
        public void Validate_WhenReconnectMaxLowerThanBase_ShouldReturnRelationshipError() {
            var options = CreateValidOptions();
            options.ReconnectBaseDelayMs = 500;
            options.ReconnectMaxDelayMs = 100;

            var errors = options.Validate();

            Assert.Contains(errors, x => x.Contains("ReconnectMaxDelayMs 必须大于或等于 ReconnectBaseDelayMs"));
        }

        /// <summary>
        /// 非法控制器 IP 应报错。
        /// </summary>
        [Fact]
        public void Validate_WhenControllerIpInvalid_ShouldReturnIpError() {
            var options = CreateValidOptions();
            options.ControllerIp = "bad-ip";

            var errors = options.Validate();

            Assert.Contains(errors, x => x.Contains("ControllerIp 必须为合法 IP 地址或空字符串"));
        }

        /// <summary>
        /// 创建合法参数样例。
        /// </summary>
        /// <returns>合法参数对象。</returns>
        private static LeadshaineEmcConnectionOptions CreateValidOptions() {
            return new LeadshaineEmcConnectionOptions {
                Enabled = true,
                ConnectionTimeoutMs = 3000,
                CardNo = 0,
                Channel = 0,
                ControllerIp = "192.168.1.2",
                InitializeRetryCount = 3,
                InitializeRetryDelayMs = 200,
                PollingIntervalMs = 100,
                ReconnectBaseDelayMs = 300,
                ReconnectMaxDelayMs = 3000
            };
        }
    }
}
