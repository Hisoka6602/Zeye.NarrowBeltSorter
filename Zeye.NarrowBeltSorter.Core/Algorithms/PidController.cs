namespace Zeye.NarrowBeltSorter.Core.Algorithms {
    /// <summary>
    /// PID 纯计算控制器。
    /// </summary>
    public sealed class PidController {
        /// <summary>
        /// 控制器参数。
        /// </summary>
        private readonly PidControllerOptions _options;

        /// <summary>
        /// 初始化 PID 控制器。
        /// </summary>
        /// <param name="options">控制参数。</param>
        /// <exception cref="ArgumentNullException">参数对象为空。</exception>
        public PidController(PidControllerOptions options) {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();
        }

        /// <summary>
        /// 根据输入与历史状态计算下一次输出。
        /// </summary>
        /// <param name="input">本次控制输入。</param>
        /// <param name="state">上次控制状态。</param>
        /// <returns>控制输出与下一状态。</returns>
        public PidControllerOutput Compute(in PidControllerInput input, in PidControllerState state) {
            // 1) 计算速度偏差与 Hz 域误差。
            var errorSpeedMmps = input.TargetSpeedMmps - input.ActualSpeedMmps;
            var errorHz = errorSpeedMmps / _options.MmpsPerHz;
            var proportional = _options.Kp * errorHz;

            // 2) 计算积分候选并执行积分限幅。
            var integralCandidate = input.FreezeIntegral
                ? state.Integral
                : state.Integral + (errorHz * _options.SamplePeriodSeconds);
            integralCandidate = Clamp(integralCandidate, _options.IntegralMin, _options.IntegralMax);

            // 3) 计算微分项，首帧时抑制微分尖峰。
            var derivative = 0m;
            if (state.Initialized) {
                var rawDerivative = (errorHz - state.LastError) / _options.SamplePeriodSeconds;
                derivative = (_options.DerivativeFilterAlpha * rawDerivative)
                    + ((1m - _options.DerivativeFilterAlpha) * state.LastDerivative);
            }

            // 4) 前馈目标频率与 PID 三项合成。
            var targetHz = input.TargetSpeedMmps / _options.MmpsPerHz;
            var integral = _options.Ki * integralCandidate;
            var derivativeTerm = _options.Kd * derivative;
            var unclamped = targetHz + proportional + integral + derivativeTerm;

            // 5) 输出限幅并识别限幅方向。
            var command = Clamp(unclamped, _options.OutputMinHz, _options.OutputMaxHz);
            var outputClamped = command != unclamped;
            var clampedToMax = outputClamped && command == _options.OutputMaxHz;
            var clampedToMin = outputClamped && command == _options.OutputMinHz;

            // 6) 条件积分 anti-windup：同向误差触发限幅时冻结积分。
            var nextIntegral = integralCandidate;
            if ((clampedToMax && errorHz > 0m) || (clampedToMin && errorHz < 0m)) {
                nextIntegral = state.Integral;
            }

            // 7) 更新状态并返回完整输出。
            var nextState = new PidControllerState(
                Integral: nextIntegral,
                LastError: errorHz,
                LastDerivative: state.Initialized ? derivative : 0m,
                Initialized: true);

            return new PidControllerOutput(
                CommandHz: command,
                ErrorSpeedMmps: errorSpeedMmps,
                Proportional: proportional,
                Integral: integral,
                Derivative: derivativeTerm,
                UnclampedHz: unclamped,
                OutputClamped: outputClamped,
                NextState: nextState);
        }

        /// <summary>
        /// 对值执行上下限约束。
        /// </summary>
        /// <param name="value">待约束值。</param>
        /// <param name="min">最小值。</param>
        /// <param name="max">最大值。</param>
        /// <returns>约束后的值。</returns>
        private static decimal Clamp(decimal value, decimal min, decimal max) {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
