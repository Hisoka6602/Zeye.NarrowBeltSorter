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
        public ValueTask<bool> WriteIoAsync(string pointId, bool value, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() {
            DisposeCalled = true;
            Status = EmcControllerStatus.Uninitialized;
            return ValueTask.CompletedTask;
        }
    }
}
