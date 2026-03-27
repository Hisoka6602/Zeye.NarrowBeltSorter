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
        /// 输出控制量下限（控制域单位）。
        /// </summary>
        public decimal OutputMinRaw { get; init; } = 0m;

        /// <summary>
        /// 输出频率上限（Hz）。
        /// </summary>
        public decimal OutputMaxRaw { get; init; } = 1000m;

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
        /// 速度误差缩放系数（默认 1，表示不做速度到控制量换算，直接按误差调节输出）。
        /// </summary>
        public decimal ErrorScale { get; init; } = 1m;

        /// <summary>
        /// 校验参数合法性。
        /// </summary>
        /// <param name="logger">用于记录异常分支日志的日志器。</param>
        /// <exception cref="ArgumentOutOfRangeException">参数超出允许范围。</exception>
        public void Validate(ILogger? logger = null) {
            if (SamplePeriodSeconds <= 0m) {
                ThrowValidationException(logger, nameof(SamplePeriodSeconds), SamplePeriodSeconds, "采样周期必须大于 0。");
            }

            if (OutputMinRaw > OutputMaxRaw) {
                ThrowValidationException(logger, nameof(OutputMinRaw), OutputMinRaw, "输出控制量下限不能大于上限。");
            }

            if (IntegralMin > IntegralMax) {
                ThrowValidationException(logger, nameof(IntegralMin), IntegralMin, "积分下限不能大于积分上限。");
            }

            if (DerivativeFilterAlpha < 0m || DerivativeFilterAlpha > 1m) {
                ThrowValidationException(logger, nameof(DerivativeFilterAlpha), DerivativeFilterAlpha, "微分滤波系数必须位于 [0, 1]。");
            }

            if (ErrorScale <= 0m) {
                ThrowValidationException(logger, nameof(ErrorScale), ErrorScale, "速度误差缩放系数必须大于 0。");
            }
        }

        /// <summary>
        /// 输出参数校验失败日志并抛出异常。
        /// </summary>
        /// <param name="logger">日志器。</param>
        /// <param name="paramName">参数名称。</param>
        /// <param name="actualValue">实际值。</param>
        /// <param name="message">异常信息。</param>
        /// <exception cref="ArgumentOutOfRangeException">参数值非法。</exception>
        private static void ThrowValidationException(ILogger? logger, string paramName, object? actualValue, string message) {
            logger?.LogError(
                "PidControllerOptions 参数校验失败 ParamName={ParamName} ActualValue={ActualValue} Message={Message}",
                paramName,
                actualValue,
                message);

            throw new ArgumentOutOfRangeException(paramName, actualValue, message);
        }
    }
}
