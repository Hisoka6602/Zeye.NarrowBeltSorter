using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.Sorting;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Options.Sorting;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Validators;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine {
    /// <summary>
    /// 双触发源配置相关回归测试。
    /// </summary>
    public sealed class LeadshaineMatureStartConfigurationTests {
        /// <summary>
        /// 分拣时序配置默认值应保持与设计一致。
        /// </summary>
        [Fact]
        public void SortingTaskTimingOptions_Defaults_ShouldMatchDesign() {
            var options = new SortingTaskTimingOptions();

            Assert.Equal(
                SortingTaskTimingOptions.DefaultParcelMatureDelayMs,
                options.ParcelMatureDelayMs);
            Assert.Equal(
                SortingTaskTimingOptions.DefaultParcelMatureStartSource,
                options.ParcelMatureStartSource);
            Assert.False(options.EnableFallbackToParcelCreateWhenLoadingTriggerMissing);
            Assert.Equal(
                SortingTaskTimingOptions.DefaultChuteOpenCloseIntervalMs,
                options.ChuteOpenCloseIntervalMs);
        }

        /// <summary>
        /// 传感器绑定校验应支持上车触发源枚举。
        /// </summary>
        [Fact]
        public void Validate_WhenUsingLoadingTriggerSensor_ShouldPass() {
            var validator = new LeadshaineSensorOptionsBindingValidator();
            var sensorOptions = new LeadshaineSensorBindingCollectionOptions {
                Sensors = [
                    new LeadshaineSensorBindingOptions {
                        SensorName = "上车触发源",
                        Type = IoPointType.LoadingTriggerSensor,
                        PointId = "I-Loading"
                    }
                ]
            };
            var pointOptions = new LeadshaineIoPointBindingCollectionOptions {
                Points = [
                    new LeadshaineIoPointBindingOption {
                        PointId = "I-Loading",
                        Binding = new LeadshaineBitBindingOption {
                            Area = "Input",
                            CardNo = 0,
                            PortNo = 0,
                            BitIndex = 1,
                            TriggerState = "High"
                        }
                    }
                ]
            };

            var errors = validator.Validate(sensorOptions, pointOptions);

            Assert.Empty(errors);
            Assert.True(Enum.IsDefined(ParcelMatureStartSource.LoadingTriggerSensor));
        }
    }
}
