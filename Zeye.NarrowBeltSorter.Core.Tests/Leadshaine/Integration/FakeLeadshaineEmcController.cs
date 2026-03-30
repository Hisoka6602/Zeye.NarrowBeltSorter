using Zeye.NarrowBeltSorter.Core.Enums.Emc;
using Zeye.NarrowBeltSorter.Core.Events.Emc;
using Zeye.NarrowBeltSorter.Core.Manager.Emc;
using Zeye.NarrowBeltSorter.Core.Models.Emc;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Integration {
    /// <summary>
    /// Leadshaine 集成测试用 EMC 控制器桩。
    /// </summary>
    public sealed class FakeLeadshaineEmcController : IEmcController {
        private readonly object _stateLock = new();
        private readonly object _writeLock = new();
        private List<IoPointInfo> _points = [];

        /// <summary>
        /// 初始化返回结果。
        /// </summary>
        public bool InitializeResult { get; set; } = true;

        /// <summary>
        /// 点位同步返回结果。
        /// </summary>
        public bool SetMonitoredResult { get; set; } = true;

        /// <summary>
        /// 初始化调用次数。
        /// </summary>
        public int InitializeCallCount { get; private set; }

        /// <summary>
        /// 点位同步调用次数。
        /// </summary>
        public int SetMonitoredCallCount { get; private set; }

        /// <summary>
        /// 每次点位同步的参数快照。
        /// </summary>
        public List<IReadOnlyCollection<string>> MonitoredPointBatches { get; } = [];

        /// <summary>
        /// 是否已执行释放。
        /// </summary>
        public bool DisposeCalled { get; private set; }

        /// <summary>
        /// 联动写入调用记录。
        /// </summary>
        public List<(string PointId, bool Value)> WriteIoCalls { get; } = [];

        /// <inheritdoc />
        public EmcControllerStatus Status { get; private set; } = EmcControllerStatus.Uninitialized;

        /// <inheritdoc />
        public int FaultCode { get; private set; }

        /// <inheritdoc />
        public IReadOnlyCollection<IoPointInfo> MonitoredIoPoints {
            get {
                lock (_stateLock) {
                    return _points.ToArray();
                }
            }
        }

        /// <inheritdoc />
        public event EventHandler<EmcStatusChangedEventArgs>? StatusChanged;

        /// <inheritdoc />
        public event EventHandler<EmcFaultedEventArgs>? Faulted;

        /// <inheritdoc />
        public event EventHandler<EmcInitializedEventArgs>? Initialized;

        /// <summary>
        /// 更新 EMC 快照点位。
        /// </summary>
        /// <param name="points">新快照点位。</param>
        public void UpdatePoints(IEnumerable<IoPointInfo> points) {
            lock (_stateLock) {
                _points = points.ToList();
            }
        }

        /// <summary>
        /// 模拟 EMC 断链，触发 StatusChanged（Disconnected）事件，用于测试 Faulted 状态流转。
        /// </summary>
        public void RaiseDisconnected() {
            var oldStatus = Status;
            Status = EmcControllerStatus.Disconnected;
            StatusChanged?.Invoke(this, new EmcStatusChangedEventArgs {
                OldStatus = oldStatus,
                NewStatus = EmcControllerStatus.Disconnected,
                ChangedAt = DateTime.Now,
                Reason = "FakeLeadshaineEmcController.RaiseDisconnected"
            });
        }

        /// <inheritdoc />
        public ValueTask<bool> InitializeAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            InitializeCallCount++;

            var oldStatus = Status;
            Status = InitializeResult ? EmcControllerStatus.Connected : EmcControllerStatus.Faulted;
            FaultCode = InitializeResult ? 0 : -1;
            StatusChanged?.Invoke(this, new EmcStatusChangedEventArgs {
                OldStatus = oldStatus,
                NewStatus = Status,
                ChangedAt = DateTime.Now,
                Reason = "FakeLeadshaineEmcController.InitializeAsync"
            });
            if (InitializeResult) {
                Initialized?.Invoke(this, new EmcInitializedEventArgs { InitializedAt = DateTime.Now });
            }
            else {
                Faulted?.Invoke(this, new EmcFaultedEventArgs {
                    FaultCode = FaultCode,
                    Message = "初始化失败",
                    FaultedAt = DateTime.Now,
                    Exception = null
                });
            }

            return ValueTask.FromResult(InitializeResult);
        }

        /// <inheritdoc />
        public ValueTask<bool> ReconnectAsync(CancellationToken cancellationToken = default) {
            return InitializeAsync(cancellationToken);
        }

        /// <inheritdoc />
        public ValueTask<bool> SetMonitoredIoPointsAsync(IReadOnlyCollection<string> pointIds, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            SetMonitoredCallCount++;
            MonitoredPointBatches.Add(pointIds.ToArray());
            return ValueTask.FromResult(SetMonitoredResult);
        }

        /// <inheritdoc />
        public bool TryGetMonitoredPoint(string pointId, out IoPointInfo info) {
            lock (_stateLock) {
                foreach (var p in _points) {
                    if (StringComparer.OrdinalIgnoreCase.Equals(p.PointId, pointId)) {
                        info = p;
                        return true;
                    }
                }

                info = default;
                return false;
            }
        }

        /// <inheritdoc />
        public ValueTask<bool> WriteIoAsync(string pointId, bool value, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_writeLock) {
                WriteIoCalls.Add((pointId, value));
            }
            return ValueTask.FromResult(true);
        }

        /// <summary>
        /// 等待写入调用数达到目标值。
        /// </summary>
        /// <param name="expectedCount">目标调用数。</param>
        /// <param name="timeoutMs">超时时间（毫秒）。</param>
        /// <returns>是否达到目标调用数。</returns>
        public bool WaitForWriteCount(int expectedCount, int timeoutMs) {
            var start = DateTime.Now;
            while ((DateTime.Now - start).TotalMilliseconds < timeoutMs) {
                lock (_writeLock) {
                    if (WriteIoCalls.Count >= expectedCount) {
                        return true;
                    }
                }

                Thread.Sleep(10);
            }

            lock (_writeLock) {
                return WriteIoCalls.Count >= expectedCount;
            }
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() {
            DisposeCalled = true;
            Status = EmcControllerStatus.Uninitialized;
            return ValueTask.CompletedTask;
        }
    }
}
