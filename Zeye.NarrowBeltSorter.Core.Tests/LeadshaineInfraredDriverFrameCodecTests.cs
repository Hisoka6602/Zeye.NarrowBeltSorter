using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Enums.Carrier;
using Zeye.NarrowBeltSorter.Core.Enums.Chutes;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Infrared;

namespace Zeye.NarrowBeltSorter.Core.Tests {

    /// <summary>
    /// Leadshaine 红外驱动帧编解码测试。
    /// </summary>
    public sealed class LeadshaineInfraredDriverFrameCodecTests {

        /// <summary>
        /// 编码成功场景应产出符合手册规则的 8 字节帧。
        /// </summary>
        [Fact]
        public async Task EncodeAsync_WithValidTimeMode_ShouldEncodeExpectedFrame() {
            var codec = CreateCodec();
            var request = new InfraredChuteOptions {
                DinChannel = 2,
                DefaultDirection = CarrierTurnDirection.Right,
                ControlMode = InfraredControlMode.Time,
                DefaultSpeedMmps = 100,
                DefaultDurationMs = 150,
                HoldDurationMs = 30,
                TriggerDelayMs = 200,
                RollerDiameterMm = 67,
                DialCode = 1
            };

            var (ok, frame) = await codec.EncodeAsync(request);

            Assert.True(ok);
            var bytes = frame.ToArray();
            Assert.Equal(new byte[] { 0xD2, 0x41, 0x64, 0x48, 0x16, 0x03, 0x1E, 0x66 }, bytes);
        }

        /// <summary>
        /// 99H 回包校验失败应返回 false。
        /// </summary>
        [Fact]
        public async Task DecodeAsync_WhenChecksumInvalid_ShouldReturnFalse() {
            var codec = CreateCodec();
            var frame = new byte[] { 0x99, 0x01, 0x00, 0x00, 0x01, 0x04, 0x01, 0x00 };

            var (ok, _) = await codec.DecodeAsync(frame);

            Assert.False(ok);
        }

        /// <summary>
        /// 99H 故障位应被提取并回填到最小配置中。
        /// </summary>
        [Fact]
        public async Task DecodeAsync_When99HContainsFaultBits_ShouldExtractFaultBits() {
            var codec = CreateCodec();
            var frame = new byte[] { 0x99, 0x41, 0x40, 0x03, 0x01, 0x04, 0x01, 0x02 };

            var (ok, options) = await codec.DecodeAsync(frame);

            Assert.True(ok);
            Assert.Equal(1, options.DinChannel);
            Assert.Equal(InfraredControlMode.Time, options.ControlMode);
            Assert.Equal((byte)0x83, options.DialCode);
        }

        /// <summary>
        /// 构建测试目标对象。
        /// </summary>
        /// <returns>编解码器实例。</returns>
        private static LeadshaineInfraredDriverFrameCodec CreateCodec() {
            var safeExecutor = new SafeExecutor(NullLogger<SafeExecutor>.Instance);
            return new LeadshaineInfraredDriverFrameCodec(safeExecutor);
        }
    }
}
