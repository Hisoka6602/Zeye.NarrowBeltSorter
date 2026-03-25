namespace Zeye.NarrowBeltSorter.Core.Utilities.LoopTrack {
    /// <summary>
    /// 雷玛 LM1000H 速度与频率换算工具。
    /// </summary>
    public static class LeiMaSpeedConverter {
        /// <summary>
        /// 速度与频率换算系数（mm/s 每 Hz）。
        /// </summary>
        public const decimal MmpsPerHz = 100m;

        /// <summary>
        /// 频率原始单位缩放（0.01Hz/Count）。
        /// </summary>
        public const decimal FrequencyRawScale = 100m;

        /// <summary>
        /// 频率转换为线速度（mm/s）。
        /// </summary>
        /// <param name="frequencyHz">频率（Hz）。</param>
        /// <returns>线速度（mm/s）。</returns>
        public static decimal HzToMmps(decimal frequencyHz) {
            return frequencyHz * MmpsPerHz;
        }

        /// <summary>
        /// 线速度转换为频率（Hz）。
        /// </summary>
        /// <param name="speedMmps">线速度（mm/s）。</param>
        /// <returns>频率（Hz）。</returns>
        public static decimal MmpsToHz(decimal speedMmps) {
            return speedMmps / MmpsPerHz;
        }

        /// <summary>
        /// 频率转换为 0.01Hz 原始计数。
        /// </summary>
        /// <param name="frequencyHz">频率（Hz）。</param>
        /// <returns>原始寄存器值。</returns>
        public static ushort HzToRawUnit(decimal frequencyHz) {
            if (frequencyHz <= 0m) {
                return 0;
            }

            var raw = (int)decimal.Round(frequencyHz * FrequencyRawScale, MidpointRounding.AwayFromZero);
            return (ushort)Math.Clamp(raw, ushort.MinValue, ushort.MaxValue);
        }

        /// <summary>
        /// 0.01Hz 原始计数转换为频率。
        /// </summary>
        /// <param name="rawUnit">原始寄存器值。</param>
        /// <returns>频率（Hz）。</returns>
        public static decimal RawUnitToHz(ushort rawUnit) {
            return rawUnit / FrequencyRawScale;
        }

        /// <summary>
        /// 线速度转换为转矩给定原始值（P3.10）。
        /// </summary>
        /// <param name="speedMmps">目标线速度（mm/s）。</param>
        /// <param name="maxOutputHz">设计最大输出频率（Hz）。</param>
        /// <param name="maxTorqueRawUnit">转矩给定最大原始值（默认 1000）。</param>
        /// <returns>P3.10 原始值。</returns>
        public static ushort MmpsToTorqueRawUnit(decimal speedMmps, decimal maxOutputHz, ushort maxTorqueRawUnit) {
            if (speedMmps <= 0m || maxOutputHz <= 0m || maxTorqueRawUnit == 0) {
                return 0;
            }

            // 步骤1：外部 mm/s 命令先转换为 Hz，保持单位语义一致。
            var targetHz = MmpsToHz(speedMmps);

            // 步骤2：按最大频率归一化后换算到 P3.10 的转矩给定范围。
            var ratio = targetHz / maxOutputHz;
            var normalized = Math.Clamp(ratio, 0m, 1m);
            var torque = (int)decimal.Round(normalized * maxTorqueRawUnit, MidpointRounding.AwayFromZero);

            return (ushort)Math.Clamp(torque, 0, maxTorqueRawUnit);
        }
    }
}
