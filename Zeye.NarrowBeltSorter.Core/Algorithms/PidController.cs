using Microsoft.Extensions.Logging;
using Zeye.NarrowBeltSorter.Core.Options.Pid;

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
        /// <param name="validationLogger">参数校验失败日志器。</param>
        /// <exception cref="ArgumentNullException">参数对象为空。</exception>
        public PidController(PidControllerOptions options, ILogger? validationLogger = null) {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _options.Validate(validationLogger);
        }

        /// <summary>
        /// 根据输入与历史状态计算下一次输出。
        /// </summary>
        /// <param name="input">本次控制输入。</param>
        /// <param name="state">上次控制状态。</param>
        /// <returns>控制输出与下一状态。</returns>
        public PidControllerOutput Compute(in PidControllerInput input, in PidControllerState state) {
            // 步骤 1) 计算速度偏差与控制域误差。
            var errorSpeedMmps = input.TargetSpeedMmps - input.ActualSpeedMmps;
            var errorOutput = errorSpeedMmps / _options.MmpsPerOutput;
            var proportional = _options.Kp * errorOutput;

            // 步骤 2) 计算积分候选并执行积分限幅。
            var integralCandidate = input.FreezeIntegral
                ? state.Integral
                : state.Integral + (errorOutput * _options.SamplePeriodSeconds);
            integralCandidate = Clamp(integralCandidate, _options.IntegralMin, _options.IntegralMax);

            // 步骤 3) 计算微分项，首帧时抑制微分尖峰。
            var derivative = 0m;
            if (state.Initialized) {
                var rawDerivative = (errorOutput - state.LastError) / _options.SamplePeriodSeconds;
                derivative = (_options.DerivativeFilterAlpha * rawDerivative)
                    + ((1m - _options.DerivativeFilterAlpha) * state.LastDerivative);
            }

            // 步骤 4) 输出仅采用 PID 增量项（当前控制对象为扭矩 raw，不叠加目标速度前馈）。
            var targetHz = 0m;
            var derivativeTerm = _options.Kd * derivative;
            var integralWithCandidate = _options.Ki * integralCandidate;
            var unclampedWithCandidate = targetHz + proportional + integralWithCandidate + derivativeTerm;

            // 步骤 5) 输出限幅并识别限幅方向。
            var commandWithCandidate = Clamp(unclampedWithCandidate, _options.OutputMinRaw, _options.OutputMaxRaw);
            var outputClampedWithCandidate = commandWithCandidate != unclampedWithCandidate;
            var clampedToMax = outputClampedWithCandidate && commandWithCandidate == _options.OutputMaxRaw;
            var clampedToMin = outputClampedWithCandidate && commandWithCandidate == _options.OutputMinRaw;

            // 步骤 6) 条件积分 anti-windup：同向误差触发限幅时冻结积分。
            var nextIntegral = integralCandidate;
            if ((clampedToMax && errorOutput > 0m) || (clampedToMin && errorOutput < 0m)) {
                nextIntegral = state.Integral;
            }

            // 步骤 7) 使用最终积分重算输出各项，保证输出字段与下一状态一致。
            var integral = _options.Ki * nextIntegral;
            var unclamped = targetHz + proportional + integral + derivativeTerm;
            var command = Clamp(unclamped, _options.OutputMinRaw, _options.OutputMaxRaw);
            var outputClamped = command != unclamped;

            // 步骤 8) 更新状态并返回完整输出。
            var nextState = new PidControllerState(
                Integral: nextIntegral,
                LastError: errorOutput,
                LastDerivative: state.Initialized ? derivative : 0m,
                Initialized: true);

            return new PidControllerOutput(
                CommandOutput: command,
                ErrorSpeedMmps: errorSpeedMmps,
                Proportional: proportional,
                Integral: integral,
                Derivative: derivativeTerm,
                UnclampedOutput: unclamped,
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
