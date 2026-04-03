using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Events.Io;
using Zeye.NarrowBeltSorter.Core.Models.Emc;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Sensor;
using System.Collections.Concurrent;
using Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Emc;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Integration {
    /// <summary>
    /// Leadshaine 传感器管理器去抖行为测试。
    /// </summary>
    public sealed class LeadshaineSensorManagerDebounceTests {
        /// <summary>
        /// 去抖窗口内重复抖动只应发布一次事件。
        /// </summary>
        [Fact]
        public async Task StartMonitoringAsync_WhenSignalBouncesWithinDebounceWindow_ShouldPublishSingleEvent() {
            var fakeEmc = new FakeLeadshaineEmcController();
            var manager = CreateManager(fakeEmc, debounceWindowMs: 300);
            var events = new ConcurrentQueue<SensorStateChangedEventArgs>();
            manager.SensorStateChanged += (_, args) => events.Enqueue(args);

            await manager.StartMonitoringAsync();

            fakeEmc.UpdatePoints([CreateInputPoint("I-01", true)]);
            await Task.Delay(60);
            fakeEmc.UpdatePoints([CreateInputPoint("I-01", false)]);
            await Task.Delay(60);
            fakeEmc.UpdatePoints([CreateInputPoint("I-01", true)]);
            await Task.Delay(60);

            Assert.Single(events);
            Assert.True(events.TryPeek(out var changedEvent));
            Assert.Equal(IoPointType.ParcelCreateSensor, changedEvent.SensorType);
            await manager.StopMonitoringAsync();
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 启动监控时应先将点位同步到 EMC。
        /// </summary>
        [Fact]
        public async Task StartMonitoringAsync_ShouldSyncSensorPointsToEmc() {
            var fakeEmc = new FakeLeadshaineEmcController();
            var manager = CreateManager(fakeEmc, debounceWindowMs: 0);

            await manager.StartMonitoringAsync();

            Assert.Equal(1, fakeEmc.SetMonitoredCallCount);
            Assert.Single(fakeEmc.MonitoredPointBatches);
            Assert.Contains("I-01", fakeEmc.MonitoredPointBatches[0], StringComparer.OrdinalIgnoreCase);
            await manager.StopMonitoringAsync();
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 轮询间隔未配置时应回退到 EMC 默认轮询间隔。
        /// </summary>
        [Fact]
        public async Task StartMonitoringAsync_WhenPollIntervalNotConfigured_ShouldUseEmcPollingInterval() {
            var fakeEmc = new FakeLeadshaineEmcController();
            const int emcPollingIntervalMs = 30;
            var manager = CreateManager(fakeEmc, debounceWindowMs: 0, pollIntervalMs: 0, emcPollingIntervalMs);
            var events = new ConcurrentQueue<SensorStateChangedEventArgs>();
            manager.SensorStateChanged += (_, args) => events.Enqueue(args);

            await manager.StartMonitoringAsync();
            fakeEmc.UpdatePoints([CreateInputPoint("I-01", true)]);
            // 轮询回退路径依赖 EmcConnection.PollingIntervalMs，预留裕量避免测试抖动。
            await Task.Delay(emcPollingIntervalMs * 2 + 10);

            Assert.Single(events);
            await manager.StopMonitoringAsync();
            await manager.DisposeAsync();
        }

        /// <summary>
        /// 创建传感器管理器。
        /// </summary>
        /// <param name="emcController">EMC 控制器测试桩。</param>
        /// <param name="debounceWindowMs">去抖窗口毫秒。</param>
        /// <param name="pollIntervalMs">传感器轮询间隔毫秒。</param>
        /// <returns>传感器管理器实例。</returns>
        private static LeadshaineSensorManager CreateManager(
            FakeLeadshaineEmcController emcController,
            int debounceWindowMs,
            int pollIntervalMs = 10,
            int emcPollingIntervalMs = 30) {
            return new LeadshaineSensorManager(
                NullLogger<LeadshaineSensorManager>.Instance,
                new SafeExecutor(NullLogger<SafeExecutor>.Instance),
                emcController,
                new LeadshaineSensorBindingCollectionOptions {
                    Sensors = [
                        new LeadshaineSensorBindingOptions {
                            SensorName = "S1",
                            SensorType = IoPointType.ParcelCreateSensor,
                            PointId = "I-01",
                            PollIntervalMs = pollIntervalMs,
                            DebounceWindowMs = debounceWindowMs
                        }
                    ]
                },
                new LeadshaineIoPointBindingCollectionOptions {
                    Points = [
                        LeadshaineEmcControllerTestFactory.CreateIoPointBinding("I-01", "Input", 0, 0, 1)
                    ]
                },
                new LeadshaineEmcConnectionOptions {
                    PollingIntervalMs = emcPollingIntervalMs
                });
        }

        /// <summary>
        /// 创建输入点位快照。
        /// </summary>
        /// <param name="pointId">点位标识。</param>
        /// <param name="value">电平值。</param>
        /// <returns>输入点位快照。</returns>
        private static IoPointInfo CreateInputPoint(string pointId, bool value) {
            return new IoPointInfo {
                PointId = pointId,
                Area = "Input",
                CardNo = 0,
                PortNo = 0,
                BitIndex = 1,
                Value = value,
                CapturedAt = DateTime.Now
            };
        }
    }
}
