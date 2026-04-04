using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Events.IoPanel;
using Zeye.NarrowBeltSorter.Core.Manager.IoPanel;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Integration {
    /// <summary>
    /// IoPanel 测试桩，用于模拟按钮按下/释放事件。
    /// </summary>
    internal sealed class FakeIoPanel : IIoPanel {
        /// <inheritdoc />
        public IoPanelMonitoringStatus Status => IoPanelMonitoringStatus.Monitoring;

        /// <inheritdoc />
        public bool IsMonitoring => true;

        /// <inheritdoc />
        public IReadOnlyCollection<string> MonitoredPointIds => [];

        /// <inheritdoc />
        public event EventHandler<IoPanelButtonPressedEventArgs>? StartButtonPressed;

        /// <inheritdoc />
        public event EventHandler<IoPanelButtonPressedEventArgs>? StopButtonPressed;

        /// <inheritdoc />
        public event EventHandler<IoPanelButtonPressedEventArgs>? EmergencyStopButtonPressed;

        /// <inheritdoc />
        public event EventHandler<IoPanelButtonPressedEventArgs>? ResetButtonPressed;

        /// <inheritdoc />
        public event EventHandler<IoPanelButtonReleasedEventArgs>? EmergencyStopButtonReleased;

        /// <inheritdoc />
        public event EventHandler<IoPanelButtonPressedEventArgs>? MaintenanceSwitchOpened;

        /// <inheritdoc />
        public event EventHandler<IoPanelButtonReleasedEventArgs>? MaintenanceSwitchClosed;

        /// <inheritdoc />
        public event EventHandler<IoPanelMonitoringStatusChangedEventArgs>? MonitoringStatusChanged;

        /// <inheritdoc />
        public event EventHandler<IoPanelFaultedEventArgs>? Faulted;

        /// <inheritdoc />
        public ValueTask StartMonitoringAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        /// <inheritdoc />
        public ValueTask StopMonitoringAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        /// <summary>
        /// 触发指定按钮的按下事件。
        /// </summary>
        /// <param name="buttonType">按钮类型。</param>
        public void RaisePressed(IoPanelButtonType buttonType) {
            var args = new IoPanelButtonPressedEventArgs("P1", "BTN", buttonType, DateTime.Now);
            switch (buttonType) {
                case IoPanelButtonType.Start:
                    StartButtonPressed?.Invoke(this, args);
                    break;
                case IoPanelButtonType.Stop:
                    StopButtonPressed?.Invoke(this, args);
                    break;
                case IoPanelButtonType.EmergencyStop:
                    EmergencyStopButtonPressed?.Invoke(this, args);
                    break;
                case IoPanelButtonType.Reset:
                    ResetButtonPressed?.Invoke(this, args);
                    break;
                case IoPanelButtonType.MaintenanceSwitch:
                    MaintenanceSwitchOpened?.Invoke(this, args);
                    break;
            }
        }

        /// <summary>
        /// 触发指定按钮的释放事件。
        /// </summary>
        /// <param name="buttonType">按钮类型。</param>
        public void RaiseReleased(IoPanelButtonType buttonType) {
            var args = new IoPanelButtonReleasedEventArgs("P1", "BTN", buttonType, DateTime.Now);
            switch (buttonType) {
                case IoPanelButtonType.EmergencyStop:
                    EmergencyStopButtonReleased?.Invoke(this, args);
                    break;
                case IoPanelButtonType.MaintenanceSwitch:
                    MaintenanceSwitchClosed?.Invoke(this, args);
                    break;
            }
        }
    }
}
