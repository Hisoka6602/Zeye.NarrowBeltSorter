using Microsoft.Extensions.Logging;

namespace Zeye.NarrowBeltSorter.Core.Options.Pid {
    /// <summary>
    /// PID 控制器参数。
    /// </summary>
    public sealed record PidControllerOptions {
        /// <summary>
        /// 比例系数。
        /// </summary>
        public decimal Kp { get; init; } = 1m;

        /// <summary>
        /// 积分系数。
        /// </summary>
        public decimal Ki { get; init; } = 0m;

        /// <summary>
        /// 微分系数。
        /// </summary>
        public decimal Kd { get; init; } = 0m;

        /// <summary>
        /// 采样周期（秒）。
        /// </summary>
        public decimal SamplePeriodSeconds { get; init; } = 0.05m;

        /// <summary>
        /// 输出频率下限（Hz）。
        /// </summary>
        public decimal OutputMinHz { get; init; } = 0m;

        /// <summary>
        /// 输出频率上限（Hz）。
        /// </summary>
        public decimal OutputMaxHz { get; init; } = 60m;

        /// <summary>
        /// 积分累计下限。
        /// </summary>
        public decimal IntegralMin { get; init; } = -10m;

        /// <summary>
        /// 积分累计上限。
        /// </summary>
        public decimal IntegralMax { get; init; } = 10m;

        /// <summary>
        /// 微分滤波系数（范围 [0, 1]）。
        /// </summary>
        public decimal DerivativeFilterAlpha { get; init; } = 0.2m;

        /// <summary>
        /// 速度到频率换算系数（每 1Hz 对应的 mm/s）。
        /// </summary>
        public decimal MmpsPerHz { get; init; } = 100m;

        /// <summary>
        /// 校验参数合法性。
        /// </summary>
        /// <param name="logger">可选的日志记录器，用于记录参数校验失败信息。</param>
        /// <exception cref="ArgumentOutOfRangeException">参数超出允许范围。</exception>
        public void Validate(ILogger? logger = null) {
            if (SamplePeriodSeconds <= 0m) {
                logger?.LogError("PID 参数校验失败：参数名={ParameterName}，参数值={ParameterValue}，违反规则={Rule}", nameof(SamplePeriodSeconds), SamplePeriodSeconds, "采样周期必须大于 0。");
                throw new ArgumentOutOfRangeException(nameof(SamplePeriodSeconds), SamplePeriodSeconds, "采样周期必须大于 0。");
            }

            if (OutputMinHz > OutputMaxHz) {
                logger?.LogError(
                    "PID 参数校验失败：参数名={ParameterName}，参数值={ParameterValue}，关联参数名={RelatedParameterName}，关联参数值={RelatedParameterValue}，违反规则={Rule}",
                    nameof(OutputMinHz),
                    OutputMinHz,
                    nameof(OutputMaxHz),
                    OutputMaxHz,
                    "输出频率下限不能大于上限。");
                throw new ArgumentOutOfRangeException(nameof(OutputMinHz), OutputMinHz, "输出频率下限不能大于上限。");
            }

            if (IntegralMin > IntegralMax) {
                logger?.LogError(
                    "PID 参数校验失败：参数名={ParameterName}，参数值={ParameterValue}，关联参数名={RelatedParameterName}，关联参数值={RelatedParameterValue}，违反规则={Rule}",
                    nameof(IntegralMin),
                    IntegralMin,
                    nameof(IntegralMax),
                    IntegralMax,
                    "积分下限不能大于积分上限。");
                throw new ArgumentOutOfRangeException(nameof(IntegralMin), IntegralMin, "积分下限不能大于积分上限。");
            }

            if (DerivativeFilterAlpha < 0m || DerivativeFilterAlpha > 1m) {
                logger?.LogError("PID 参数校验失败：参数名={ParameterName}，参数值={ParameterValue}，违反规则={Rule}", nameof(DerivativeFilterAlpha), DerivativeFilterAlpha, "微分滤波系数必须位于 [0, 1]。");
                throw new ArgumentOutOfRangeException(nameof(DerivativeFilterAlpha), DerivativeFilterAlpha, "微分滤波系数必须位于 [0, 1]。");
            }

            if (MmpsPerHz <= 0m) {
                logger?.LogError("PID 参数校验失败：参数名={ParameterName}，参数值={ParameterValue}，违反规则={Rule}", nameof(MmpsPerHz), MmpsPerHz, "速度频率换算系数必须大于 0。");
                throw new ArgumentOutOfRangeException(nameof(MmpsPerHz), MmpsPerHz, "速度频率换算系数必须大于 0。");
            }
        }
    }
}
