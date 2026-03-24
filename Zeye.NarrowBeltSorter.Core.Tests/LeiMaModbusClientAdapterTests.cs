using Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// 雷码 Modbus 客户端适配器参数校验测试。
    /// </summary>
    public sealed class LeiMaModbusClientAdapterTests {
        /// <summary>
        /// 从站地址越界时应抛出参数异常。
        /// </summary>
        [Theory]
        [InlineData((byte)0)]
        [InlineData((byte)248)]
        public void Constructor_WhenSlaveAddressOutOfRange_ShouldThrow(byte slaveAddress) {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                _ = new LeiMaModbusClientAdapter("127.0.0.1:502", slaveAddress);
            });

            Assert.Equal("slaveAddress", exception.ParamName);
        }

        /// <summary>
        /// TCP 地址为空时，构造阶段应抛出参数异常。
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public void Constructor_WhenRemoteHostInvalid_ShouldThrow(string remoteHost) {
            var exception = Assert.Throws<ArgumentException>(() =>
            {
                _ = new LeiMaModbusClientAdapter(remoteHost, 1);
            });

            Assert.Equal("remoteHost", exception.ParamName);
        }

        /// <summary>
        /// 超时参数非法时应抛出参数异常。
        /// </summary>
        [Fact]
        public void Constructor_WhenTimeoutInvalid_ShouldThrow() {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                _ = new LeiMaModbusClientAdapter("127.0.0.1:502", 1, 0, 1);
            });

            Assert.Equal("modbusTimeoutMilliseconds", exception.ParamName);
        }

        /// <summary>
        /// 重试次数非法时应抛出参数异常。
        /// </summary>
        [Fact]
        public void Constructor_WhenRetryCountInvalid_ShouldThrow() {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                _ = new LeiMaModbusClientAdapter("127.0.0.1:502", 1, 1000, -1);
            });

            Assert.Equal("retryCount", exception.ParamName);
        }

    }
}
