using System.Runtime.CompilerServices;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.Chutes;
using Zeye.NarrowBeltSorter.Core.Enums.Carrier;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Protocols;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Infrared {

    /// <summary>
    /// Leadshaine 红外驱动器 D1/D2 执行帧编解码器。
    /// </summary>
    public sealed class LeadshaineInfraredDriverFrameCodec : IInfraredDriverFrameCodec {

        /// <summary>
        /// 协议固定帧长。
        /// </summary>
        private const int FrameLength = 8;

        /// <summary>
        /// DIN1 执行帧功能码。
        /// </summary>
        private const byte Din1FunctionCode = 0xD1;

        /// <summary>
        /// DIN2 执行帧功能码。
        /// </summary>
        private const byte Din2FunctionCode = 0xD2;

        /// <summary>
        /// 99H 回包功能码。
        /// </summary>
        private const byte AckFunctionCode = 0x99;

        /// <summary>
        /// 协议速度增益默认值，单位 rpm/raw。
        /// </summary>
        private const decimal RpmPerSpeedRaw = 6m;

        /// <summary>
        /// 协议延时刻度，单位 ms/tick。
        /// </summary>
        private const decimal DelayTickMs = 10m;

        /// <summary>
        /// 协议时间模式刻度，单位 ms/tick。
        /// </summary>
        private const decimal DurationTickMs = 10m;

        /// <summary>
        /// 协议位置增益默认值，单位 r/raw。
        /// </summary>
        private const decimal CirclePerPositionRaw = 0.048m;

        /// <summary>
        /// 协议文档使用的圆周率近似值。
        /// </summary>
        private const decimal ProtocolPi = 3.14m;

        /// <summary>
        /// 协议最小加速度，单位 mm/s²。
        /// 文档公式：1.5 + 0.05 * X，单位换算后为 1500 + 50 * X。
        /// </summary>
        private const decimal MinAccelerationMmps2 = 1500m;

        /// <summary>
        /// 协议加速度步进，单位 mm/s²。
        /// </summary>
        private const decimal AccelerationStepMmps2 = 50m;

        /// <summary>
        /// 协议最大加速度，单位 mm/s²。
        /// X 最大 127，对应 1500 + 50 * 127 = 7850。
        /// </summary>
        private const decimal MaxAccelerationMmps2 = 7850m;

        /// <summary>
        /// 方向位掩码。
        /// </summary>
        private const byte DirectionMask = 0x40;

        /// <summary>
        /// 地址位掩码。
        /// </summary>
        private const byte AddressMask = 0x3F;

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
        /// 编码 D1/D2 执行帧。
        /// </summary>
        /// <param name="request">红外格口配置。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>编码结果。</returns>
        public async ValueTask<(bool, ReadOnlyMemory<byte>)> EncodeAsync(
            InfraredChuteOptions request,
            CancellationToken cancellationToken = default) {
            var execution = await _safeExecutor.ExecuteAsync(
                _ => ValueTask.FromResult(EncodeCore(request)),
                "LeadshaineInfraredDriverFrameCodec.EncodeAsync",
                (false, ReadOnlyMemory<byte>.Empty),
                cancellationToken).ConfigureAwait(false);

            return execution.Success ? execution.Result : (false, ReadOnlyMemory<byte>.Empty);
        }

        /// <summary>
        /// 当前契约返回 InfraredChuteOptions，无法准确承载 99H 回包语义。
        /// 因此此处仅做最小化合法性校验，不再伪造业务配置对象。
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
        /// 编码核心流程。
        /// </summary>
        /// <param name="request">红外格口配置。</param>
        /// <returns>编码结果。</returns>
        private static (bool, ReadOnlyMemory<byte>) EncodeCore(InfraredChuteOptions request) {
            if (request is null) {
                return (false, ReadOnlyMemory<byte>.Empty);
            }

            if (!TryMapDinToFunctionCode(request.DinChannel, out var functionCode)) {
                return (false, ReadOnlyMemory<byte>.Empty);
            }

            if (!TryBuildSpeedRawValue(request, out var speedRaw)) {
                return (false, ReadOnlyMemory<byte>.Empty);
            }

            if (!TryBuildDelayRawValue(request, out var delayRaw)) {
                return (false, ReadOnlyMemory<byte>.Empty);
            }

            if (!TryBuildRunRawValue(request, out var runRaw)) {
                return (false, ReadOnlyMemory<byte>.Empty);
            }

            if (!TryBuildAccelerationRawValue(request, out var accelerationRaw)) {
                return (false, ReadOnlyMemory<byte>.Empty);
            }

            var frame = new byte[FrameLength];
            frame[0] = functionCode;
            frame[1] = BuildByte2(request.DefaultDirection, request.DialCode);
            frame[2] = speedRaw;
            frame[3] = (byte)(delayRaw & 0x7F);
            frame[4] = (byte)(runRaw & 0x7F);
            frame[5] = BuildByte6(request.ControlMode, delayRaw, runRaw);
            frame[6] = accelerationRaw;
            frame[7] = ComputeXor(frame.AsSpan(1, 6));

            return (true, frame);
        }

        /// <summary>
        /// 解析核心流程。
        /// 仅校验 99H 回包合法性；由于当前接口契约不适合承载 ACK 语义，因此不再构造伪业务对象。
        /// </summary>
        /// <param name="frame">回包帧。</param>
        /// <returns>解析结果。</returns>
        private static (bool, InfraredChuteOptions) DecodeCore(ReadOnlySpan<byte> frame) {
            if (frame.Length != FrameLength) {
                return (false, default!);
            }

            if (frame[0] != AckFunctionCode) {
                return (false, default!);
            }

            // 文档中的 99H 回包校验为 Byte2 ~ Byte4 异或。
            var expectedChecksum = (byte)(frame[1] ^ frame[2] ^ frame[3]);
            if (frame[7] != expectedChecksum) {
                return (false, default!);
            }

            // 当前接口契约无法准确表达 ACK 状态，因此仅返回 false，避免伪造业务配置。
            return (false, default!);
        }

        /// <summary>
        /// 将 DIN 通道映射到 D1/D2 执行帧功能码。
        /// </summary>
        /// <param name="dinChannel">DIN 通道号。</param>
        /// <param name="functionCode">输出功能码。</param>
        /// <returns>映射是否成功。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryMapDinToFunctionCode(int dinChannel, out byte functionCode) {
            functionCode = dinChannel switch {
                1 => Din1FunctionCode,
                2 => Din2FunctionCode,
                _ => 0
            };

            return functionCode != 0;
        }

        /// <summary>
        /// 构造 Byte2：方向位 + 拨码地址。
        /// 现场若方向位与业务方向枚举相反，应仅调整此映射。
        /// </summary>
        /// <param name="direction">方向。</param>
        /// <param name="dialCode">拨码值。</param>
        /// <returns>Byte2。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte BuildByte2(CarrierTurnDirection direction, byte dialCode) {
            var directionBit = direction == CarrierTurnDirection.Right ? DirectionMask : (byte)0x00;
            return (byte)(directionBit | (dialCode & AddressMask));
        }

        /// <summary>
        /// 构造 Byte6：模式位 + 运行值高位 + 延时高位。
        /// 当前未提供 PI 配置，因此 PI 固定为 0。
        /// </summary>
        /// <param name="controlMode">控制模式。</param>
        /// <param name="delayRaw">延时原始值。</param>
        /// <param name="runRaw">时间/距离原始值。</param>
        /// <returns>Byte6。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte BuildByte6(InfraredControlMode controlMode, int delayRaw, int runRaw) {
            var modeBit = controlMode == InfraredControlMode.Position ? 0x04 : 0x00;
            var runHighBit = ((runRaw >> 7) & 0x01) << 1;
            var delayHighBit = (delayRaw >> 7) & 0x01;
            return (byte)(modeBit | runHighBit | delayHighBit);
        }

        /// <summary>
        /// 构造速度原始值。
        /// 计算过程：mm/s -> rpm -> raw。
        /// </summary>
        /// <param name="request">红外格口配置。</param>
        /// <param name="speedRaw">速度原始值。</param>
        /// <returns>构造是否成功。</returns>
        private static bool TryBuildSpeedRawValue(InfraredChuteOptions request, out byte speedRaw) {
            speedRaw = 0;

            if (request.DefaultSpeedMmps <= 0) {
                return false;
            }

            if (request.RollerDiameterMm <= 0) {
                return false;
            }

            var circumference = request.RollerDiameterMm * ProtocolPi;
            if (circumference <= 0) {
                return false;
            }

            var rpm = request.DefaultSpeedMmps * 60m / circumference;
            var raw = rpm / RpmPerSpeedRaw;

            return TryConvertTo7Bit(raw, out speedRaw);
        }

        /// <summary>
        /// 构造延时原始值。
        /// 协议默认 10ms/tick。
        /// </summary>
        /// <param name="request">红外格口配置。</param>
        /// <param name="delayRaw">延时原始值。</param>
        /// <returns>构造是否成功。</returns>
        private static bool TryBuildDelayRawValue(InfraredChuteOptions request, out int delayRaw) {
            delayRaw = 0;

            if (request.TriggerDelayMs < 0) {
                return false;
            }

            var raw = request.TriggerDelayMs / DelayTickMs;
            return TryConvertTo8Bit(raw, out delayRaw);
        }

        /// <summary>
        /// 构造时间/距离原始值。
        /// 时间模式使用毫秒转 tick；位置模式使用线性距离转圈数再转 raw。
        /// </summary>
        /// <param name="request">红外格口配置。</param>
        /// <param name="runRaw">运行原始值。</param>
        /// <returns>构造是否成功。</returns>
        private static bool TryBuildRunRawValue(InfraredChuteOptions request, out int runRaw) {
            runRaw = 0;

            if (request.ControlMode == InfraredControlMode.Time) {
                if (!request.DefaultDurationMs.HasValue || request.DefaultDurationMs.Value <= 0) {
                    return false;
                }

                var durationRaw = request.DefaultDurationMs.Value / DurationTickMs;
                return TryConvertTo8Bit(durationRaw, out runRaw);
            }

            if (!request.DefaultDistanceMm.HasValue || request.DefaultDistanceMm.Value <= 0) {
                return false;
            }

            if (request.RollerDiameterMm <= 0) {
                return false;
            }

            var circumference = request.RollerDiameterMm * ProtocolPi;
            if (circumference <= 0) {
                return false;
            }

            var circles = request.DefaultDistanceMm.Value / circumference;
            var positionRaw = circles / CirclePerPositionRaw;

            return TryConvertTo8Bit(positionRaw, out runRaw);
        }

        /// <summary>
        /// 构造加速度原始值。
        /// 配置字段单位为 mm/s²，协议公式单位为 m/s²，已在此处完成换算。
        /// </summary>
        /// <param name="request">红外格口配置。</param>
        /// <param name="accelerationRaw">加速度原始值。</param>
        /// <returns>构造是否成功。</returns>
        private static bool TryBuildAccelerationRawValue(InfraredChuteOptions request, out byte accelerationRaw) {
            accelerationRaw = 0;

            if (request.AccelerationMmps2 <= 0) {
                return false;
            }

            var accelerationMmps2 = (decimal)request.AccelerationMmps2;
            if (accelerationMmps2 < MinAccelerationMmps2 || accelerationMmps2 > MaxAccelerationMmps2) {
                return false;
            }

            var raw = (accelerationMmps2 - MinAccelerationMmps2) / AccelerationStepMmps2;
            return TryConvertTo7Bit(raw, out accelerationRaw);
        }

        /// <summary>
        /// 转换为 7 位无符号原始值。
        /// 超出协议范围时直接返回失败，避免静默截断。
        /// </summary>
        /// <param name="value">输入值。</param>
        /// <param name="raw">输出原始值。</param>
        /// <returns>转换是否成功。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryConvertTo7Bit(decimal value, out byte raw) {
            raw = 0;

            var rounded = decimal.Round(value, 0, MidpointRounding.AwayFromZero);
            if (rounded < 0 || rounded > 127) {
                return false;
            }

            raw = (byte)rounded;
            return true;
        }

        /// <summary>
        /// 转换为 8 位无符号原始值。
        /// 超出协议范围时直接返回失败，避免静默截断。
        /// </summary>
        /// <param name="value">输入值。</param>
        /// <param name="raw">输出原始值。</param>
        /// <returns>转换是否成功。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryConvertTo8Bit(decimal value, out int raw) {
            raw = 0;

            var rounded = decimal.Round(value, 0, MidpointRounding.AwayFromZero);
            if (rounded < 0 || rounded > 255) {
                return false;
            }

            raw = (int)rounded;
            return true;
        }

        /// <summary>
        /// 计算 Byte2 ~ Byte7 异或校验。
        /// </summary>
        /// <param name="data">待校验字节段。</param>
        /// <returns>异或结果。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ComputeXor(ReadOnlySpan<byte> data) {
            var xor = (byte)0;
            foreach (var item in data) {
                xor ^= item;
            }

            return xor;
        }
    }
}
