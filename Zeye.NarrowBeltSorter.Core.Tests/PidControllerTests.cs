using Zeye.NarrowBeltSorter.Core.Algorithms;
using Zeye.NarrowBeltSorter.Core.Options.Pid;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// PidController 计算行为测试。
    /// </summary>
    public sealed class PidControllerTests {
        /// <summary>
        /// 构造基础参数。
        /// </summary>
        /// <returns>测试参数对象。</returns>
        private static PidControllerOptions CreateDefaultOptions() {
            return new PidControllerOptions {
                Kp = 1m,
                Ki = 1m,
                Kd = 1m,
                SamplePeriodSeconds = 1m,
                OutputMinHz = -10m,
                OutputMaxHz = 10m,
                IntegralMin = -2m,
                IntegralMax = 2m,
                DerivativeFilterAlpha = 1m,
                MmpsPerHz = 100m
            };
        }

        /// <summary>
        /// 采样周期小于等于 0 时必须抛出异常。
        /// </summary>
        [Fact]
        public void Validate_WhenSamplePeriodLessOrEqualZero_ShouldThrow() {
            var options = CreateDefaultOptions() with { SamplePeriodSeconds = 0m };

            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());

            Assert.Equal(nameof(PidControllerOptions.SamplePeriodSeconds), exception.ParamName);
        }

        /// <summary>
        /// 输出下限大于上限时必须抛出异常。
        /// </summary>
        [Fact]
        public void Validate_WhenOutputMinGreaterThanOutputMax_ShouldThrow() {
            var options = CreateDefaultOptions() with { OutputMinHz = 11m, OutputMaxHz = 10m };

            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());

            Assert.Equal(nameof(PidControllerOptions.OutputMinHz), exception.ParamName);
        }

        /// <summary>
        /// 积分下限大于上限时必须抛出异常。
        /// </summary>
        [Fact]
        public void Validate_WhenIntegralMinGreaterThanIntegralMax_ShouldThrow() {
            var options = CreateDefaultOptions() with { IntegralMin = 1m, IntegralMax = 0m };

            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());

            Assert.Equal(nameof(PidControllerOptions.IntegralMin), exception.ParamName);
        }

        /// <summary>
        /// 微分滤波系数超界时必须抛出异常。
        /// </summary>
        [Theory]
        [InlineData(-0.01)]
        [InlineData(1.01)]
        public void Validate_WhenDerivativeFilterAlphaOutOfRange_ShouldThrow(decimal alpha) {
            var options = CreateDefaultOptions() with { DerivativeFilterAlpha = alpha };

            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());

            Assert.Equal(nameof(PidControllerOptions.DerivativeFilterAlpha), exception.ParamName);
        }

        /// <summary>
        /// 速度频率换算系数非正时必须抛出异常。
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Validate_WhenMmpsPerHzLessOrEqualZero_ShouldThrow(decimal mmpsPerHz) {
            var options = CreateDefaultOptions() with { MmpsPerHz = mmpsPerHz };

            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());

            Assert.Equal(nameof(PidControllerOptions.MmpsPerHz), exception.ParamName);
        }

        /// <summary>
        /// 首帧微分项应为 0。
        /// </summary>
        [Fact]
        public void Compute_WhenFirstFrame_DerivativeShouldBeZero() {
            var controller = new PidController(CreateDefaultOptions());
            var input = new PidControllerInput(TargetSpeedMmps: 100m, ActualSpeedMmps: 0m, FreezeIntegral: false);
            var state = new PidControllerState(Integral: 0m, LastError: 10m, LastDerivative: 10m, Initialized: false);

            var output = controller.Compute(in input, in state);

            Assert.Equal(0m, output.Derivative);
            Assert.Equal(0m, output.NextState.LastDerivative);
        }

        /// <summary>
        /// 输出应正确执行上下限限幅。
        /// </summary>
        [Fact]
        public void Compute_WhenOutputOutOfRange_ShouldClampToBounds() {
            var options = CreateDefaultOptions() with { Kp = 2m, Ki = 0m, Kd = 0m, OutputMinHz = 0m, OutputMaxHz = 1m };
            var controller = new PidController(options);

            var highInput = new PidControllerInput(TargetSpeedMmps: 1000m, ActualSpeedMmps: 0m, FreezeIntegral: false);
            var highState = new PidControllerState(Integral: 0m, LastError: 0m, LastDerivative: 0m, Initialized: true);
            var highOutput = controller.Compute(in highInput, in highState);

            Assert.Equal(1m, highOutput.CommandHz);
            Assert.True(highOutput.OutputClamped);

            var lowInput = new PidControllerInput(TargetSpeedMmps: -1000m, ActualSpeedMmps: 0m, FreezeIntegral: false);
            var lowOutput = controller.Compute(in lowInput, in highState);

            Assert.Equal(0m, lowOutput.CommandHz);
            Assert.True(lowOutput.OutputClamped);
        }

        /// <summary>
        /// 同向误差触发限幅时应启用 anti-windup 保持积分不变。
        /// </summary>
        [Fact]
        public void Compute_WhenClampedWithSameDirectionError_ShouldKeepIntegral() {
            var options = CreateDefaultOptions() with {
                Kp = 1m,
                Ki = 10m,
                Kd = 0m,
                OutputMinHz = 0m,
                OutputMaxHz = 0.5m,
                IntegralMin = -100m,
                IntegralMax = 100m
            };
            var controller = new PidController(options);
            var state = new PidControllerState(Integral: 2m, LastError: 0m, LastDerivative: 0m, Initialized: true);
            var input = new PidControllerInput(TargetSpeedMmps: 100m, ActualSpeedMmps: 0m, FreezeIntegral: false);

            var output = controller.Compute(in input, in state);

            Assert.True(output.OutputClamped);
            Assert.Equal(2m, output.NextState.Integral);
            Assert.Equal(20m, output.Integral);
            Assert.Equal(22m, output.UnclampedHz);
        }

        /// <summary>
        /// 冻结积分时积分状态保持不变。
        /// </summary>
        [Fact]
        public void Compute_WhenFreezeIntegralTrue_ShouldNotChangeIntegral() {
            var controller = new PidController(CreateDefaultOptions());
            var state = new PidControllerState(Integral: 1.25m, LastError: 0m, LastDerivative: 0m, Initialized: true);
            var input = new PidControllerInput(TargetSpeedMmps: 200m, ActualSpeedMmps: 0m, FreezeIntegral: true);

            var output = controller.Compute(in input, in state);

            Assert.Equal(1.25m, output.NextState.Integral);
        }
    }
}
