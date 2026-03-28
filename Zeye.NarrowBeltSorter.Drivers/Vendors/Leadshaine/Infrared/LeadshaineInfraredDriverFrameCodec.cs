using Zeye.NarrowBeltSorter.Core.Enums.Carrier;
using Zeye.NarrowBeltSorter.Core.Enums.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Protocols;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;
using Zeye.NarrowBeltSorter.Core.Utilities;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Infrared {

    /// <summary>
    /// Leadshaine 红外驱动器 8 字节帧编解码器。
    /// </summary>
    public sealed class LeadshaineInfraredDriverFrameCodec : IInfraredDriverFrameCodec {

        private const int FrameLength = 8;
        private const byte AckFunctionCode = 0x99;
        private const decimal RpmPerSpeedRaw = 6m;
        private const decimal DelayTickMs = 10m;
        private const decimal DurationTickMs = 10m;
        private const decimal CirclePerPositionRaw = 0.048m;
        private readonly SafeExecutor _safeExecutor;

        /// <summary>
        /// 初始化编解码器。
        /// </summary>
        /// <param name="safeExecutor">统一安全执行器。</param>
        public LeadshaineInfraredDriverFrameCodec(SafeExecutor safeExecutor) {
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
        }

        /// <summary>
        /// 厂商编码。
        /// </summary>
        public string VendorCode => "Leadshaine";

        /// <summary>
        /// 按 LDC-FJ-RF 8 字节帧规则进行编码。
        /// </summary>
        /// <param name="request">红外格口参数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>编码结果。</returns>
        public ValueTask<(bool, ReadOnlyMemory<byte>)> EncodeAsync(
            InfraredChuteOptions request,
            CancellationToken cancellationToken = default) {
            if (request is null) {
                return ValueTask.FromResult((false, ReadOnlyMemory<byte>.Empty));
            }

            if (!TryMapDinToFunctionCode(request.DinChannel, out var functionCode)) {
                return ValueTask.FromResult((false, ReadOnlyMemory<byte>.Empty));
            }

            if (!TryBuildRunRawValue(request, out var runRaw)) {
                return ValueTask.FromResult((false, ReadOnlyMemory<byte>.Empty));
            }

            if (!TryBuildSpeedRawValue(request, out var speedRaw)) {
                return ValueTask.FromResult((false, ReadOnlyMemory<byte>.Empty));
            }

            // 按协议延时刻度 TDK=10ms/tick 转换为原始值。
            var delayRaw = ToProtocolTickFromMilliseconds(request.TriggerDelayMs, DelayTickMs);
            var accelRaw = ToRaw7Bit(request.HoldDurationMs);

            var frame = new byte[FrameLength];
            frame[0] = functionCode;
            frame[1] = BuildByte2(request.DefaultDirection, request.DialCode);
            frame[2] = speedRaw;
            frame[3] = (byte)(delayRaw & 0x7F);
            frame[4] = (byte)(runRaw & 0x7F);
            frame[5] = BuildByte6(request.ControlMode, delayRaw, runRaw);
            frame[6] = accelRaw;
            frame[7] = ComputeXor(frame.AsSpan(1, 6));

            return ValueTask.FromResult((true, (ReadOnlyMemory<byte>)frame));
        }

        /// <summary>
        /// 仅解析 99H 回包并校验 Byte2~Byte4 异或。
        /// </summary>
        /// <param name="frame">回包帧。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>解析结果。</returns>
        public async ValueTask<(bool, InfraredChuteOptions)> DecodeAsync(
            ReadOnlyMemory<byte> frame,
            CancellationToken cancellationToken = default) {
            var execution = await _safeExecutor.ExecuteAsync(
                _ => ValueTask.FromResult(DecodeCore(frame.Span)),
                "LeadshaineInfraredDriverFrameCodec.DecodeAsync",
                (false, default(InfraredChuteOptions)!),
                cancellationToken).ConfigureAwait(false);

            return execution.Success ? execution.Result : (false, default!);
        }

        /// <summary>
        /// 执行 99H 回包解析核心流程。
        /// </summary>
        /// <param name="frame">回包帧。</param>
        /// <returns>解析结果。</returns>
        private static (bool, InfraredChuteOptions) DecodeCore(ReadOnlySpan<byte> frame) {
            // 步骤1：校验帧长、功能码与 Byte2~Byte4 异或校验，过滤无效回包。
            if (frame.Length != FrameLength) {
                return (false, default!);
            }

            if (frame[0] != AckFunctionCode) {
                return (false, default!);
            }

            var expectedChecksum = (byte)(frame[1] ^ frame[2] ^ frame[3]);
            if (expectedChecksum != frame[7]) {
                return (false, default!);
            }

            // 步骤2：解析地址并映射回 DIN 通道，确保地址可路由到受支持功能码。
            var receiverAddress = ParseReceiverAddress(frame[1], frame[2]);
            var dinChannel = MapAddressToDin(receiverAddress);
            if (!TryMapDinToFunctionCode(dinChannel, out _)) {
                return (false, default!);
            }

            // 步骤3：提取接收器与驱动器故障位，并构建最小化配置对象返回上层。
            var receiverFault = ((frame[1] & 0x40) != 0) || ((frame[2] & 0x40) != 0);
            var driverFaultBits = (byte)(frame[3] & 0x7F);
            var encodedFaultBits = receiverFault ? (byte)(driverFaultBits | 0x80) : driverFaultBits;

            var options = new InfraredChuteOptions {
                DinChannel = dinChannel,
                DefaultDirection = CarrierTurnDirection.Left,
                ControlMode = InfraredControlMode.Time,
                DefaultSpeedMmps = 0,
                DefaultDurationMs = 0,
                HoldDurationMs = 0,
                TriggerDelayMs = 0,
                RollerDiameterMm = 0,
                DialCode = encodedFaultBits
            };

            return (true, options);
        }

        /// <summary>
        /// 将 DIN 通道映射到功能码。
        /// </summary>
        /// <param name="dinChannel">DIN 通道。</param>
        /// <param name="functionCode">输出功能码。</param>
        /// <returns>映射是否成功。</returns>
        private static bool TryMapDinToFunctionCode(int dinChannel, out byte functionCode) {
            functionCode = dinChannel switch {
                1 => 0xD1,
                2 => 0xD2,
                3 => 0xD3,
                4 => 0xD4,
                _ => 0
            };

            return functionCode != 0;
        }

        /// <summary>
        /// 构造第二字节（方向 + 地址）。
        /// </summary>
        /// <param name="direction">方向。</param>
        /// <param name="dialCode">地址拨码。</param>
        /// <returns>第二字节。</returns>
        private static byte BuildByte2(CarrierTurnDirection direction, byte dialCode) {
            var directionBit = direction == CarrierTurnDirection.Right ? 0x40 : 0x00;
            return (byte)(directionBit | (dialCode & 0x3F));
        }

        /// <summary>
        /// 构造第六字节（模式 + 高位拼接）。
        /// </summary>
        /// <param name="controlMode">控制模式。</param>
        /// <param name="delayRaw">延时原始值。</param>
        /// <param name="runRaw">时间/圈数原始值。</param>
        /// <returns>第六字节。</returns>
        private static byte BuildByte6(InfraredControlMode controlMode, int delayRaw, int runRaw) {
            var modeBit = controlMode == InfraredControlMode.Position ? 0x04 : 0x00;
            var runHighBit = ((runRaw >> 7) & 0x01) << 1;
            var delayHighBit = (delayRaw >> 7) & 0x01;
            return (byte)(modeBit | runHighBit | delayHighBit);
        }

        /// <summary>
        /// 计算异或校验。
        /// </summary>
        /// <param name="data">待校验数据。</param>
        /// <returns>异或结果。</returns>
        private static byte ComputeXor(ReadOnlySpan<byte> data) {
            var xor = (byte)0;
            foreach (var item in data) {
                xor ^= item;
            }

            return xor;
        }

        /// <summary>
        /// 将地址映射回 DIN 通道。
        /// </summary>
        /// <param name="address">接收模块地址。</param>
        /// <returns>DIN 通道。</returns>
        private static int MapAddressToDin(int address) {
            if (address <= 0) {
                return 1;
            }

            var remainder = address % 4;
            return remainder == 0 ? 4 : remainder;
        }

        /// <summary>
        /// 解析接收模块地址。
        /// </summary>
        /// <param name="byte2">第二字节。</param>
        /// <param name="byte3">第三字节。</param>
        /// <returns>地址值。</returns>
        private static int ParseReceiverAddress(byte byte2, byte byte3) {
            var low = byte2 & 0x3F;
            var high = byte3 & 0x3F;
            return low + (high << 6);
        }

        /// <summary>
        /// 计算第三字节的速度原始值（毫米每秒转每分钟转速，再转协议原始值）。
        /// </summary>
        /// <param name="request">红外格口参数。</param>
        /// <param name="speedRaw">输出原始值。</param>
        /// <returns>是否成功。</returns>
        private static bool TryBuildSpeedRawValue(InfraredChuteOptions request, out byte speedRaw) {
            speedRaw = 0;
            var rollerDiameter = request.RollerDiameterMm;
            if (rollerDiameter <= 0) {
                return false;
            }

            var circumference = rollerDiameter * (decimal)Math.PI;
            if (circumference <= 0) {
                return false;
            }

            // 步骤1：线速度 mm/s 转电机转速 rpm。
            var rpm = request.DefaultSpeedMmps * 60m / circumference;
            // 步骤2：按 VK=6 rpm/raw 转换为协议速度原始值。
            var rawSpeed = rpm / RpmPerSpeedRaw;
            speedRaw = ToRaw7Bit(rawSpeed);
            return true;
        }

        /// <summary>
        /// 计算第五字节的时间/圈数原始值。
        /// </summary>
        /// <param name="request">红外格口参数。</param>
        /// <param name="runRaw">输出原始值。</param>
        /// <returns>是否成功。</returns>
        private static bool TryBuildRunRawValue(InfraredChuteOptions request, out int runRaw) {
            runRaw = 0;
            if (request.ControlMode == InfraredControlMode.Time) {
                if (!request.DefaultDurationMs.HasValue) {
                    return false;
                }

                // 按协议时间刻度 TK=10ms/tick 转换为原始值。
                runRaw = ToProtocolTickFromMilliseconds(request.DefaultDurationMs.Value, DurationTickMs);
                return true;
            }

            if (!request.DefaultDistanceMm.HasValue || request.RollerDiameterMm <= 0) {
                return false;
            }

            var circumference = request.RollerDiameterMm * (decimal)Math.PI;
            if (circumference <= 0) {
                return false;
            }

            // 步骤1：将线性距离 mm 换算为电机圈数 r。
            var circles = request.DefaultDistanceMm.Value / circumference;
            // 步骤2：按 PK=0.048r/raw 转换为协议位置原始值。
            runRaw = ToRaw8Bit(circles / CirclePerPositionRaw);
            return true;
        }

        /// <summary>
        /// 将毫秒转换为协议刻度值。
        /// </summary>
        /// <param name="milliseconds">毫秒值。</param>
        /// <param name="tickMs">单刻度毫秒值。</param>
        /// <returns>协议刻度。</returns>
        private static int ToProtocolTickFromMilliseconds(int milliseconds, decimal tickMs) {
            if (tickMs <= 0) {
                return 0;
            }

            var tick = (decimal)milliseconds / tickMs;
            return ToRaw8Bit(tick);
        }

        /// <summary>
        /// 转换为 7 位原始值。
        /// </summary>
        /// <param name="value">输入值。</param>
        /// <returns>7 位原始值。</returns>
        private static byte ToRaw7Bit(decimal value) {
            var rounded = decimal.Round(value, 0, MidpointRounding.AwayFromZero);
            var clamped = Math.Clamp((int)rounded, 0, 127);
            return (byte)clamped;
        }

        /// <summary>
        /// 转换为 8 位原始值。
        /// </summary>
        /// <param name="value">输入值。</param>
        /// <returns>8 位原始值。</returns>
        private static int ToRaw8Bit(decimal value) {
            var rounded = decimal.Round(value, 0, MidpointRounding.AwayFromZero);
            return Math.Clamp((int)rounded, 0, 255);
        }

    }
}
