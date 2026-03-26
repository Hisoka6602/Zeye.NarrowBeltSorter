namespace Zeye.NarrowBeltSorter.Core.Utilities.LoopTrack {

    /// <summary>
    /// 雷玛 LM1000H 寄存器与命令常量。
    /// </summary>
    public static class LeiMaRegisters {

        /// <summary>
        /// 控制命令寄存器（2000H）。
        /// </summary>
        public const ushort Command = 0x2000;

        /// <summary>
        /// 运行状态寄存器（C0.32 / 5020H）。
        /// </summary>
        public const ushort RunStatus = 0x5020;

        /// <summary>
        /// 故障代码寄存器（3100H）。
        /// </summary>
        public const ushort AlarmCode = 0x3100;

        /// <summary>
        /// 最大输出频率寄存器（P0.04 / F004H，0.01Hz/Count）。
        /// </summary>
        public const ushort MaxOutputFrequency = 0xF004;

        /// <summary>
        /// 限速频率寄存器（F007H，0.01Hz/Count）。
        /// 仅保留为扩展参数，不可用于 SetTargetSpeedAsync 主链路。
        /// </summary>
        public const ushort FrequencySetpoint = 0xF007;

        /// <summary>
        /// 转矩给定值寄存器（P3.10）。
        /// </summary>
        public const ushort TorqueSetpoint = 0x030A;

        /// <summary>
        /// 运行频率监视寄存器（5000H）。
        /// </summary>
        public const ushort RunningFrequency = 0x5000;

        /// <summary>
        /// 编码器反馈速度寄存器（501AH）。
        /// </summary>
        public const ushort EncoderFeedbackSpeed = 0x501A;

        /// <summary>
        /// 正转运行命令值。
        /// </summary>
        public const ushort CommandForwardRun = 1;

        /// <summary>
        /// 减速停机命令值。
        /// </summary>
        public const ushort CommandDecelerateStop = 5;

        /// <summary>
        /// 故障复位命令值。
        /// </summary>
        public const ushort CommandAlarmReset = 7;
    }
}
