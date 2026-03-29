using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.SiemensS7;
using Zeye.NarrowBeltSorter.Core.Options.SiemensS7;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// 西门子 S7 配置校验测试。
    /// </summary>
    public sealed class SiemensS7OptionsTests {
        /// <summary>
        /// 合法配置应通过校验。
        /// </summary>
        [Fact]
        public void Validate_WithValidOptions_ShouldReturnNoErrors() {
            var options = CreateValidOptions();

            var errors = options.Validate();

            Assert.Empty(errors);
        }

        /// <summary>
        /// 点位编号重复时应返回错误。
        /// </summary>
        [Fact]
        public void Validate_WithDuplicatedPointId_ShouldReturnErrors() {
            var options = CreateValidOptions();
            options.PointBindings.Add(new SiemensS7PointBindingOptions {
                PointId = 1,
                Name = "重复点位",
                PointType = IoPointType.ParcelCreateSensor,
                Area = SiemensS7AddressArea.Input,
                ByteOffset = 1,
                BitIndex = 1
            });

            var errors = options.Validate();

            Assert.NotEmpty(errors);
            Assert.Contains(errors, message => message.Contains("PointId=1", StringComparison.Ordinal));
        }

        /// <summary>
        /// 传感器映射到输出区时应返回错误。
        /// </summary>
        [Fact]
        public void Validate_WithSensorBindingToOutputArea_ShouldReturnErrors() {
            var options = CreateValidOptions();
            options.PointBindings[0] = options.PointBindings[0] with { Area = SiemensS7AddressArea.Output };

            var errors = options.Validate();

            Assert.NotEmpty(errors);
            Assert.Contains(errors, message => message.Contains("不能映射到输出区", StringComparison.Ordinal));
        }

        /// <summary>
        /// 传感器点位缺失绑定时应返回错误。
        /// </summary>
        [Fact]
        public void Validate_WithSensorPointMissingBinding_ShouldReturnErrors() {
            var options = CreateValidOptions();
            options.Sensors[0] = options.Sensors[0] with { PointId = 999 };

            var errors = options.Validate();

            Assert.NotEmpty(errors);
            Assert.Contains(errors, message => message.Contains("未在 PointBindings 中定义", StringComparison.Ordinal));
        }

        /// <summary>
        /// 构建合法配置样本。
        /// </summary>
        /// <returns>合法配置样本。</returns>
        private static SiemensS7Options CreateValidOptions() {
            return new SiemensS7Options {
                Enabled = true,
                EmcConnection = new S7EmcConnectionOptions {
                    Endpoint = "192.168.1.10",
                    CpuType = "S71500",
                    Rack = 0,
                    Slot = 1,
                    IoPollIntervalMs = 100,
                    ReconnectMinDelayMs = 100,
                    ReconnectMaxDelayMs = 2000,
                    ReconnectBackoffFactor = 2.0m
                },
                PointBindings = new List<SiemensS7PointBindingOptions> {
                    new() {
                        PointId = 1,
                        Name = "包裹创建传感器",
                        PointType = IoPointType.ParcelCreateSensor,
                        TriggerState = IoState.High,
                        Area = SiemensS7AddressArea.Input,
                        ByteOffset = 0,
                        BitIndex = 0
                    }
                },
                Sensors = new List<SiemensS7SensorOptions> {
                    new() {
                        Name = "包裹创建传感器",
                        PointId = 1,
                        DebounceWindowMs = 50
                    }
                }
            };
        }
    }
}
