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
                DefaultSpeedMmps = 2100,
                DefaultDurationMs = 150,
                HoldDurationMs = 30,
                TriggerDelayMs = 200,
                RollerDiameterMm = 67,
                DialCode = 1
            };

            var (ok, frame) = await codec.EncodeAsync(request);

            Assert.True(ok);
            var bytes = frame.ToArray();
            // 期望帧：D2(DIN2) 41(右转+地址1) 64(速度100raw) 14(延时200ms->20刻度) 0F(时长150ms->15刻度) 00(模式+高位) 1E(加减速参数) 20(校验)。
            Assert.Equal(new byte[] { 0xD2, 0x41, 0x64, 0x14, 0x0F, 0x00, 0x1E, 0x20 }, bytes);
        }

        /// <summary>
        /// 速度单位不一致场景应执行 mm/s 到协议 raw 的换算。
        /// </summary>
        [Fact]
        public async Task EncodeAsync_WhenSpeedUnitDiffers_ShouldConvertMmpsToRawSpeed() {
            var codec = CreateCodec();
            var request = new InfraredChuteOptions {
                DinChannel = 1,
                DefaultDirection = CarrierTurnDirection.Left,
                ControlMode = InfraredControlMode.Time,
                DefaultSpeedMmps = 630,
                DefaultDurationMs = 100,
                HoldDurationMs = 0,
                TriggerDelayMs = 0,
                RollerDiameterMm = 67,
                DialCode = 1
            };

            var (ok, frame) = await codec.EncodeAsync(request);

            Assert.True(ok);
            var bytes = frame.ToArray();
            // 630mm/s ÷ (π×67/60) -> 180rpm；180rpm ÷ 6 -> 30raw -> 0x1E。
            Assert.Equal(0x1E, bytes[2]);
        }

        /// <summary>
        /// 时间参数应从毫秒转换为协议时间刻度。
        /// </summary>
        [Fact]
        public async Task EncodeAsync_WhenTimeUnitDiffers_ShouldConvertMillisecondsToTicks() {
            var codec = CreateCodec();
            var request = new InfraredChuteOptions {
                DinChannel = 3,
                DefaultDirection = CarrierTurnDirection.Left,
                ControlMode = InfraredControlMode.Time,
                DefaultSpeedMmps = 600,
                DefaultDurationMs = 100,
                HoldDurationMs = 0,
                TriggerDelayMs = 20,
                RollerDiameterMm = 67,
                DialCode = 1
            };

            var (ok, frame) = await codec.EncodeAsync(request);

            Assert.True(ok);
            var bytes = frame.ToArray();
            // TriggerDelayMs=20ms -> 2tick（TDK=10ms）-> 0x02；DefaultDurationMs=100ms -> 10tick（TK=10ms）-> 0x0A。
            Assert.Equal(0x02, bytes[3]);
            Assert.Equal(0x0A, bytes[4]);
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
