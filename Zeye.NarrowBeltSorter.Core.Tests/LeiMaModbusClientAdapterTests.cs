using System.Reflection;
using Zeye.NarrowBeltSorter.Drivers.Vendors.LeiMa;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// 雷玛 Modbus 客户端适配器帧构造测试。
    /// </summary>
    public sealed class LeiMaModbusClientAdapterTests {
        /// <summary>
        /// 读寄存器请求帧应符合 RTU 协议与 CRC 口径。
        /// </summary>
        [Fact]
        public void BuildReadHoldingRegisterFrame_ShouldMatchExpectedRtuFrame() {
            var frame = InvokePrivateStatic<byte[]>(
                nameof(LeiMaModbusClientAdapter),
                "BuildReadHoldingRegisterFrame",
                (byte)0x01,
                (ushort)0x3000);

            Assert.Equal(new byte[] { 0x01, 0x03, 0x30, 0x00, 0x00, 0x01, 0x8B, 0x0A }, frame);
        }

        /// <summary>
        /// 写单寄存器请求帧应符合 RTU 协议与 CRC 口径。
        /// </summary>
        [Fact]
        public void BuildWriteSingleRegisterFrame_ShouldMatchExpectedRtuFrame() {
            var frame = InvokePrivateStatic<byte[]>(
                nameof(LeiMaModbusClientAdapter),
                "BuildWriteSingleRegisterFrame",
                (byte)0x01,
                (ushort)0x2000,
                (ushort)0x0001);

            Assert.Equal(new byte[] { 0x01, 0x06, 0x20, 0x00, 0x00, 0x01, 0x43, 0xCA }, frame);
        }

        /// <summary>
        /// 从站地址越界时应抛出参数异常。
        /// </summary>
        [Theory]
        [InlineData((byte)0)]
        [InlineData((byte)248)]
        public void Constructor_WhenSlaveAddressOutOfRange_ShouldThrow(byte slaveAddress) {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new LeiMaModbusClientAdapter("COM1", slaveAddress));

            Assert.Equal("slaveAddress", exception.ParamName);
        }

        /// <summary>
        /// 调用私有静态方法。
        /// </summary>
        /// <typeparam name="T">返回值类型。</typeparam>
        /// <param name="typeName">类型名。</param>
        /// <param name="methodName">方法名。</param>
        /// <param name="parameters">参数数组。</param>
        /// <returns>方法返回值。</returns>
        private static T InvokePrivateStatic<T>(string typeName, string methodName, params object[] parameters) {
            var type = typeof(LeiMaModbusClientAdapter);
            Assert.Equal(typeName, type.Name);

            var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var result = method!.Invoke(null, parameters);
            Assert.NotNull(result);

            return (T)result!;
        }
    }
}
