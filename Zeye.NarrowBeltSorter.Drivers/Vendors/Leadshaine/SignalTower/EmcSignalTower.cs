using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Zeye.NarrowBeltSorter.Core.Enums.Io;
using Zeye.NarrowBeltSorter.Core.Enums.Emc;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Events.Emc;
using Zeye.NarrowBeltSorter.Core.Manager.Emc;
using Zeye.NarrowBeltSorter.Core.Enums.Device;
using Zeye.NarrowBeltSorter.Core.Models.Sensor;
using Zeye.NarrowBeltSorter.Core.Enums.SignalTower;
using Zeye.NarrowBeltSorter.Core.Events.SignalTower;
using Zeye.NarrowBeltSorter.Core.Manager.SignalTower;
using Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc.Options;
using Zeye.NarrowBeltSorter.Core.Options.SignalTower.Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.SignalTower {

    /// <summary>
    /// 基于 EMC 输出点位实现的信号塔。
    /// </summary>
    public sealed class EmcSignalTower : ISignalTower {
        private readonly object _stateLock = new();
        private readonly ILogger<EmcSignalTower> _logger;
        private readonly SafeExecutor _executor;
        private readonly IEmcController _emcController;
        private readonly string _redLightPointId;
        private readonly string _yellowLightPointId;
        private readonly string _greenLightPointId;
        private readonly string _buzzerPointId;
        private readonly bool _isRedLightEnabled;
        private readonly bool _isYellowLightEnabled;
        private readonly bool _isGreenLightEnabled;
        private readonly bool _isBuzzerEnabled;

        public EmcSignalTower(
            ILogger<EmcSignalTower> logger,
            SafeExecutor safeExecutor,
            IEmcController emcController,
            LeadshaineSignalTowerOptions signalTowerOptions,
            LeadshainePointBindingCollectionOptions pointOptions) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _executor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _emcController = emcController ?? throw new ArgumentNullException(nameof(emcController));
            var options = signalTowerOptions ?? throw new ArgumentNullException(nameof(signalTowerOptions));
            var points = pointOptions ?? throw new ArgumentNullException(nameof(pointOptions));

            Id = options.Id;
            Name = options.Name;
            _redLightPointId = options.RedLightPointId;
            _yellowLightPointId = options.YellowLightPointId;
            _greenLightPointId = options.GreenLightPointId;
            _buzzerPointId = options.BuzzerPointId;

            _isRedLightEnabled = !IsDeprecatedPointId(_redLightPointId);
            _isYellowLightEnabled = !IsDeprecatedPointId(_yellowLightPointId);
            _isGreenLightEnabled = !IsDeprecatedPointId(_greenLightPointId);
            _isBuzzerEnabled = !IsDeprecatedPointId(_buzzerPointId);

            RedLightIo = _isRedLightEnabled
                ? ResolveSensorInfo(points, _redLightPointId)
                : BuildDeprecatedSensorInfo();
            YellowLightIo = _isYellowLightEnabled
                ? ResolveSensorInfo(points, _yellowLightPointId)
                : BuildDeprecatedSensorInfo();
            GreenLightIo = _isGreenLightEnabled
                ? ResolveSensorInfo(points, _greenLightPointId)
                : BuildDeprecatedSensorInfo();
            BuzzerIo = _isBuzzerEnabled
                ? ResolveSensorInfo(points, _buzzerPointId)
                : BuildDeprecatedSensorInfo();

            LightStatus = SignalTowerLightStatus.Off;
            BuzzerStatus = BuzzerStatus.Off;
            ConnectionStatus = MapConnectionStatus(_emcController.Status);
            if (!_isRedLightEnabled) {
                _logger.LogInformation("信号塔红灯已弃用: TowerId={TowerId}, RedLightPointId={RedLightPointId}。", Id, _redLightPointId);
            }
            if (!_isYellowLightEnabled) {
                _logger.LogInformation("信号塔黄灯已弃用: TowerId={TowerId}, YellowLightPointId={YellowLightPointId}。", Id, _yellowLightPointId);
            }
            if (!_isGreenLightEnabled) {
                _logger.LogInformation("信号塔绿灯已弃用: TowerId={TowerId}, GreenLightPointId={GreenLightPointId}。", Id, _greenLightPointId);
            }
            if (!_isBuzzerEnabled) {
                _logger.LogInformation("信号塔蜂鸣器已弃用: TowerId={TowerId}, BuzzerPointId={BuzzerPointId}。", Id, _buzzerPointId);
            }

            _emcController.StatusChanged += HandleEmcStatusChanged;
        }

        public long Id { get; }

        public string Name { get; }

        public SensorInfo RedLightIo { get; }

        public SensorInfo GreenLightIo { get; }

        public SensorInfo YellowLightIo { get; }

        public SensorInfo BuzzerIo { get; }

        public SignalTowerLightStatus LightStatus { get; private set; }

        public BuzzerStatus BuzzerStatus { get; private set; }

        public DeviceConnectionStatus ConnectionStatus { get; private set; }

        public event EventHandler<SignalTowerLightStatusChangedEventArgs>? LightStatusChanged;

        public event EventHandler<SignalTowerBuzzerStatusChangedEventArgs>? BuzzerStatusChanged;

        public event EventHandler<SignalTowerConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

        public async ValueTask<bool> SetLightStatusAsync(
            SignalTowerLightStatus lightStatus,
            CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            var oldStatus = LightStatus;

            var success = await _executor.ExecuteAsync(async ct => {
                await WriteLightStateAsync(lightStatus, ct).ConfigureAwait(false);
                lock (_stateLock) {
                    LightStatus = lightStatus;
                }
            }, $"SignalTower.SetLightStatus:{Id}:{lightStatus}", cancellationToken).ConfigureAwait(false);

            if (!success || oldStatus == lightStatus) {
                return success;
            }

            _executor.PublishEventAsync(LightStatusChanged, this, new SignalTowerLightStatusChangedEventArgs {
                SignalTowerId = Id,
                OldStatus = oldStatus,
                NewStatus = lightStatus,
                ChangedAt = DateTime.Now
            }, "EmcSignalTower.LightStatusChanged");
            return true;
        }

        /// <summary>
        /// 设置蜂鸣器状态。
        /// </summary>
        /// <param name="buzzerStatus">目标蜂鸣器状态。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否设置成功。</returns>
        public async ValueTask<bool> SetBuzzerStatusAsync(BuzzerStatus buzzerStatus, CancellationToken cancellationToken = default) {
            // 步骤1：先执行取消校验并处理蜂鸣器弃用分支。
            cancellationToken.ThrowIfCancellationRequested();
            if (!_isBuzzerEnabled) {
                if (buzzerStatus != BuzzerStatus.Off) {
                    _logger.LogWarning("信号塔蜂鸣器已弃用，忽略蜂鸣器状态写入: TowerId={TowerId}, RequestedStatus={RequestedStatus}。", Id, buzzerStatus);
                    return false;
                }

                return true;
            }

            // 步骤2：调用 EMC 写入蜂鸣器点位并在成功后更新内存状态。
            var oldStatus = BuzzerStatus;

            var success = await _executor.ExecuteAsync(async ct => {
                var writeSuccess = await _emcController.WriteIoAsync(_buzzerPointId, buzzerStatus != BuzzerStatus.On, ct).ConfigureAwait(false);
                if (!writeSuccess) {
                    throw new InvalidOperationException($"信号塔蜂鸣器写入失败，PointId={_buzzerPointId}。");
                }

                lock (_stateLock) {
                    BuzzerStatus = buzzerStatus;
                }
            }, $"SignalTower.SetBuzzerStatus:{Id}:{buzzerStatus}", cancellationToken).ConfigureAwait(false);

            // 步骤3：状态有变化时发布蜂鸣器状态事件。
            if (!success || oldStatus == buzzerStatus) {
                return success;
            }

            _executor.PublishEventAsync(BuzzerStatusChanged, this, new SignalTowerBuzzerStatusChangedEventArgs {
                SignalTowerId = Id,
                OldStatus = oldStatus,
                NewStatus = buzzerStatus,
                ChangedAt = DateTime.Now
            }, "EmcSignalTower.BuzzerStatusChanged");
            return true;
        }

        public ValueTask<bool> BlinkLightAsync(
            SignalTowerLightStatus lightStatus,
            TimeSpan onDuration,
            TimeSpan offDuration,
            int repeatCount,
            CancellationToken cancellationToken = default) {
            if (repeatCount <= 0) {
                return ValueTask.FromResult(false);
            }

            var segments = Enumerable.Range(0, repeatCount)
                .Select(_ => (OnDuration: onDuration, OffDuration: offDuration))
                .ToArray();
            return BlinkLightAsync(lightStatus, segments, cancellationToken);
        }

        public async ValueTask<bool> BlinkLightAsync(
            SignalTowerLightStatus lightStatus,
            IReadOnlyList<(TimeSpan OnDuration, TimeSpan OffDuration)> segments,
            CancellationToken cancellationToken = default) {
            if (segments is null || segments.Count == 0) {
                return false;
            }

            foreach (var (onDuration, offDuration) in segments) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await SetLightStatusAsync(lightStatus, cancellationToken).ConfigureAwait(false)) {
                    return false;
                }

                await Task.Delay(onDuration, cancellationToken).ConfigureAwait(false);
                if (!await SetLightStatusAsync(SignalTowerLightStatus.Off, cancellationToken).ConfigureAwait(false)) {
                    return false;
                }

                await Task.Delay(offDuration, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        public ValueTask<bool> BlinkBuzzerAsync(
            TimeSpan onDuration,
            TimeSpan offDuration,
            int repeatCount,
            CancellationToken cancellationToken = default) {
            if (repeatCount <= 0) {
                return ValueTask.FromResult(false);
            }

            var segments = Enumerable.Range(0, repeatCount)
                .Select(_ => (OnDuration: onDuration, OffDuration: offDuration))
                .ToArray();
            return BlinkBuzzerAsync(segments, cancellationToken);
        }

        public async ValueTask<bool> BlinkBuzzerAsync(
            IReadOnlyList<(TimeSpan OnDuration, TimeSpan OffDuration)> segments,
            CancellationToken cancellationToken = default) {
            if (segments is null || segments.Count == 0) {
                return false;
            }
            if (!_isBuzzerEnabled) {
                return true;
            }
            foreach (var (onDuration, offDuration) in segments) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await SetBuzzerStatusAsync(BuzzerStatus.On, cancellationToken).ConfigureAwait(false)) {
                    return false;
                }

                await Task.Delay(onDuration, cancellationToken).ConfigureAwait(false);
                if (!await SetBuzzerStatusAsync(BuzzerStatus.Off, cancellationToken).ConfigureAwait(false)) {
                    return false;
                }

                await Task.Delay(offDuration, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        /// <summary>
        /// 批次写入灯光状态。
        /// </summary>
        /// <param name="lightStatus">目标灯光状态。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        private async ValueTask WriteLightStateAsync(SignalTowerLightStatus lightStatus, CancellationToken cancellationToken) {
            var red = lightStatus == SignalTowerLightStatus.Red;
            var yellow = lightStatus == SignalTowerLightStatus.Yellow;
            var green = lightStatus == SignalTowerLightStatus.Green;

            var redWritten = !_isRedLightEnabled || await _emcController.WriteIoAsync(_redLightPointId, !red, cancellationToken).ConfigureAwait(false);
            var yellowWritten = !_isYellowLightEnabled || await _emcController.WriteIoAsync(_yellowLightPointId, !yellow, cancellationToken).ConfigureAwait(false);
            var greenWritten = !_isGreenLightEnabled || await _emcController.WriteIoAsync(_greenLightPointId, !green, cancellationToken).ConfigureAwait(false);
            if (!redWritten || !yellowWritten || !greenWritten) {
                throw new InvalidOperationException(
                    $"信号塔灯光写入失败。Red={_redLightPointId}:{redWritten}, Yellow={_yellowLightPointId}:{yellowWritten}, Green={_greenLightPointId}:{greenWritten}。");
            }
        }

        /// <summary>
        /// 处理 EMC 连接状态变化并同步信号塔连接状态。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="args">状态变化事件参数。</param>
        private void HandleEmcStatusChanged(object? sender, EmcStatusChangedEventArgs args) {
            var newStatus = MapConnectionStatus(args.NewStatus);
            DeviceConnectionStatus oldStatus;

            lock (_stateLock) {
                oldStatus = ConnectionStatus;
                if (oldStatus == newStatus) {
                    return;
                }

                ConnectionStatus = newStatus;
            }

            _logger.LogInformation("信号塔连接状态变化: TowerId={TowerId}, Old={OldStatus}, New={NewStatus}。", Id, oldStatus, newStatus);
            _executor.PublishEventAsync(ConnectionStatusChanged, this, new SignalTowerConnectionStatusChangedEventArgs {
                SignalTowerId = Id,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                ChangedAt = DateTime.Now
            }, "EmcSignalTower.ConnectionStatusChanged");
        }

        /// <summary>
        /// 将 EMC 状态映射为设备连接状态。
        /// </summary>
        /// <param name="status">EMC 状态。</param>
        /// <returns>设备连接状态。</returns>
        private static DeviceConnectionStatus MapConnectionStatus(EmcControllerStatus status) {
            return status switch {
                EmcControllerStatus.Connecting => DeviceConnectionStatus.Connecting,
                EmcControllerStatus.Connected => DeviceConnectionStatus.Connected,
                EmcControllerStatus.Faulted => DeviceConnectionStatus.Faulted,
                _ => DeviceConnectionStatus.Disconnected
            };
        }

        /// <summary>
        /// 从点位配置解析传感器信息。
        /// </summary>
        /// <param name="pointOptions">点位绑定集合。</param>
        /// <param name="pointId">点位编号。</param>
        /// <returns>传感器信息。</returns>
        private static SensorInfo ResolveSensorInfo(LeadshainePointBindingCollectionOptions pointOptions, string pointId) {
            var point = pointOptions.Points.FirstOrDefault(x => string.Equals(x.PointId, pointId, StringComparison.OrdinalIgnoreCase));
            if (point is null) {
                throw new InvalidOperationException($"信号塔点位未配置或不存在：PointId={pointId}。请先在 Leadshaine:PointBindings:Points 中定义。");
            }

            var pointType = string.Equals(point.Binding.Area, "Output", StringComparison.OrdinalIgnoreCase)
                ? IoPointType.PanelButton
                : IoPointType.ParcelCreateSensor;

            return new SensorInfo {
                Point = point.Binding.BitIndex,
                Type = pointType,
                State = IoState.Low
            };
        }

        /// <summary>
        /// 判定点位是否为弃用占位标识。
        /// </summary>
        /// <param name="pointId">点位编号。</param>
        /// <returns>是否弃用。</returns>
        private static bool IsDeprecatedPointId(string pointId) {
            return string.Equals(pointId?.Trim(), "0", StringComparison.Ordinal);
        }

        /// <summary>
        /// 构建弃用点位的占位传感器信息。
        /// </summary>
        /// <returns>占位传感器信息。</returns>
        private static SensorInfo BuildDeprecatedSensorInfo() {
            return new SensorInfo {
                Point = -1,
                Type = IoPointType.PanelButton,
                State = IoState.Low
            };
        }
    }
}
