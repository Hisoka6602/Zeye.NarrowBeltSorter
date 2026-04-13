using System;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Enums.Track;
using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.Track;
using Zeye.NarrowBeltSorter.Core.Events.System;
using Zeye.NarrowBeltSorter.Core.Events.Carrier;
using Zeye.NarrowBeltSorter.Core.Manager.System;
using Zeye.NarrowBeltSorter.Core.Manager.Carrier;
using Zeye.NarrowBeltSorter.Core.Enums.SignalTower;
using Zeye.NarrowBeltSorter.Core.Manager.SignalTower;
using Zeye.NarrowBeltSorter.Core.Manager.TrackSegment;
using Zeye.NarrowBeltSorter.Core.Options.Emc.Leadshaine;

namespace Zeye.NarrowBeltSorter.Execution.Services;

public sealed class SignalTowerHostedService : BackgroundService {

    /// <summary>检修状态黄灯闪烁周期（毫秒，亮/灭各一次）。</summary>
    private const int MaintenanceYellowBlinkIntervalMs = 300;

    private readonly ILogger<SignalTowerHostedService> _logger;
    private readonly SafeExecutor _safeExecutor;
    private readonly ISystemStateManager _systemStateManager;
    private readonly ICarrierManager _carrierManager;
    private readonly ISignalTower _signalTower;
    private readonly IOptionsMonitor<LeadshaineIoPanelStateTransitionOptions> _optionsMonitor;
    private readonly ILoopTrackManagerAccessor _loopTrackAccessor;
    private readonly object _buzzerLock = new();

    /// <summary>通用蜂鸣取消令牌源，任意新状态到来时重置（取消旧会话）。</summary>
    private CancellationTokenSource? _buzzerCts;

    /// <summary>蜂鸣代际号，每次新蜂鸣会话自增；用于防止旧任务关闭更新状态的蜂鸣。</summary>
    private int _buzzerGeneration;

    /// <summary>已订阅稳速事件的管理器实例，用于退订时定向清理。</summary>
    private ILoopTrackManager? _subscribedManager;

    private EventHandler<StateChangeEventArgs>? _stateChangedHandler;
    private EventHandler<CarrierRingBuiltEventArgs>? _ringBuiltHandler;
    private EventHandler<ILoopTrackManager?>? _managerChangedHandler;
    private EventHandler<LoopTrackStabilizationStatusChangedEventArgs>? _stabilizationStatusChangedHandler;

    /// <summary>当前环轨稳速状态，由 StabilizationStatusChanged 事件实时更新。</summary>
    private LoopTrackStabilizationStatus _loopTrackStabilizationStatus;

    /// <summary>
    /// 初始化信号塔托管服务。
    /// </summary>
    public SignalTowerHostedService(
        ILogger<SignalTowerHostedService> logger,
        SafeExecutor safeExecutor,
        ISystemStateManager systemStateManager,
        ICarrierManager carrierManager,
        ISignalTower signalTower,
        IOptionsMonitor<LeadshaineIoPanelStateTransitionOptions> optionsMonitor,
        ILoopTrackManagerAccessor loopTrackAccessor) {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
        _systemStateManager = systemStateManager ?? throw new ArgumentNullException(nameof(systemStateManager));
        _carrierManager = carrierManager ?? throw new ArgumentNullException(nameof(carrierManager));
        _signalTower = signalTower ?? throw new ArgumentNullException(nameof(signalTower));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _loopTrackAccessor = loopTrackAccessor ?? throw new ArgumentNullException(nameof(loopTrackAccessor));
    }

    /// <summary>
    /// 订阅事件并保活，服务停止时自动退订。
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        SubscribeEvents();
        try {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            // 正常停止，忽略取消异常。
        }
        finally {
            UnsubscribeEvents();
        }
    }

    /// <summary>
    /// 停止时取消活跃蜂鸣并退订事件。
    /// </summary>
    public override Task StopAsync(CancellationToken cancellationToken) {
        CancelBuzzer();
        return base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// 订阅系统状态变更、小车建环事件与环轨管理器变更事件。
    /// </summary>
    private void SubscribeEvents() {
        _stateChangedHandler = (_, args) => OnStateChanged(args);
        _ringBuiltHandler = (_, _) => OnRingBuilt();
        _managerChangedHandler = (_, manager) => OnLoopTrackManagerChanged(manager);
        _systemStateManager.StateChanged += _stateChangedHandler;
        _carrierManager.RingBuilt += _ringBuiltHandler;
        _loopTrackAccessor.ManagerChanged += _managerChangedHandler;
        // 若服务启动时管理器已就绪，立即完成首次订阅。
        if (_loopTrackAccessor.Manager is { } current) {
            OnLoopTrackManagerChanged(current);
        }
    }

    /// <summary>
    /// 环轨管理器实例变更处理：管理器就绪时订阅稳速状态事件并同步初始状态，清空时完成退订。
    /// </summary>
    /// <param name="manager">新管理器实例；null 表示已清空。</param>
    private void OnLoopTrackManagerChanged(ILoopTrackManager? manager) {
        // 步骤1：清理旧管理器的稳速状态订阅。
        if (_subscribedManager is not null && _stabilizationStatusChangedHandler is not null) {
            _subscribedManager.StabilizationStatusChanged -= _stabilizationStatusChangedHandler;
            _stabilizationStatusChangedHandler = null;
        }
        _subscribedManager = null;

        if (manager is null) {
            return;
        }

        // 步骤2：订阅新管理器稳速状态变更事件，并同步当前稳速状态。
        _logger.LogInformation("SignalTowerHostedService 检测到环轨管理器已就绪，Track={TrackName}。", manager.TrackName);
        _loopTrackStabilizationStatus = manager.StabilizationStatus;
        _stabilizationStatusChangedHandler = (_, args) => {
            _loopTrackStabilizationStatus = args.NewStatus;
        };
        manager.StabilizationStatusChanged += _stabilizationStatusChangedHandler;
        _subscribedManager = manager;
    }

    /// <summary>
    /// 系统状态变更处理：先取消旧蜂鸣会话，再按新状态驱动灯光与蜂鸣。
    /// </summary>
    private async void OnStateChanged(StateChangeEventArgs args) {
        // 步骤1：取消旧蜂鸣任务，获取新会话的代际号与取消令牌。
        var (gen, token) = StartNewBuzzerSession();

        // 步骤2：根据新状态驱动对应信号塔输出。
        switch (args.NewState) {
            case SystemState.Paused:
            case SystemState.Booting:
            case SystemState.Ready:
                // 关灯并关闭蜂鸣。
                _ = _safeExecutor.ExecuteAsync(async () => {
                    await _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Off).ConfigureAwait(false);
                    await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.Off).ConfigureAwait(false);
                }, "SignalTower.SetLight.Off");
                break;

            case SystemState.EmergencyStop:
                _ = _safeExecutor.ExecuteAsync(async () => {
                    // 步骤a：亮红灯并开蜂鸣。
                    await _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Red).ConfigureAwait(false);
                    await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.On).ConfigureAwait(false);
                    try {
                        // 步骤b：等待 2 秒，可被新状态取消。
                        await Task.Delay(2000, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) {
                        // 被新状态取消，不关闭蜂鸣，由新状态决定蜂鸣。
                        return;
                    }
                    // 步骤c：仅当本代际仍为最新时关闭蜂鸣，防止覆盖新状态的蜂鸣。
                    if (Volatile.Read(ref _buzzerGeneration) == gen) {
                        await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.Off).ConfigureAwait(false);
                    }
                }, "SignalTower.EmergencyStop");
                break;

            case SystemState.StartupWarning:
                _ = _safeExecutor.ExecuteAsync(
                    () => _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Yellow).AsTask(),
                    "SignalTower.SetLight.Yellow");
                _ = _safeExecutor.ExecuteAsync(async () => {
                    // 步骤a：仅最新蜂鸣会话可驱动蜂鸣器，避免旧会话并发覆盖。
                    if (!IsLatestBuzzerGeneration(gen)) {
                        return;
                    }

                    // 步骤b：开蜂鸣。
                    await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.On).ConfigureAwait(false);
                    var startupWarningDurationMs = Math.Max(1, _optionsMonitor.CurrentValue.StartupWarningDurationMs);
                    try {
                        // 步骤c：等待配置时长，可被新状态取消。
                        await Task.Delay(startupWarningDurationMs, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) {
                        if (!IsLatestBuzzerGeneration(gen)) {
                            _logger.LogDebug("启动预警蜂鸣会话已被新代际替换，跳过蜂鸣关闭。");
                            return;
                        }

                        _logger.LogInformation("启动预警蜂鸣已取消，执行蜂鸣器关闭。");
                        await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.Off).ConfigureAwait(false);
                        return;
                    }

                    if (IsLatestBuzzerGeneration(gen)) {
                        await _signalTower.SetBuzzerStatusAsync(BuzzerStatus.Off).ConfigureAwait(false);
                    }
                }, "SignalTower.StartupWarningBuzzer");
                break;

            case SystemState.Running:
                _ = _safeExecutor.ExecuteAsync(
                    () => _signalTower.SetBuzzerStatusAsync(BuzzerStatus.Off).AsTask(),
                    "SignalTower.SetBuzzer.Off.Running");
                // 如果小车已建环，等待环轨稳速后切换绿灯；否则等待建环事件触发后再切换绿灯。
                if (_carrierManager.IsRingBuilt) {
                    try {
                        // 步骤a：轮询等待环轨稳速，可被新状态的蜂鸣会话取消。
                        while (!token.IsCancellationRequested &&
                               _loopTrackStabilizationStatus != LoopTrackStabilizationStatus.Stabilized) {
                            await Task.Delay(200, token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) {
                        _logger.LogDebug("运行态等待稳速已被新状态取消。");
                        return;
                    }

                    if (token.IsCancellationRequested) {
                        break;
                    }

                    // 步骤b：环轨已稳速，切换绿灯。
                    _ = _safeExecutor.ExecuteAsync(
                        () => _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Green).AsTask(),
                        "SignalTower.SetLight.Green");
                }
                break;

            case SystemState.Maintenance:
                _ = _safeExecutor.ExecuteAsync(
                    () => _signalTower.SetBuzzerStatusAsync(BuzzerStatus.Off).AsTask(),
                    "SignalTower.SetBuzzer.Off.Maintenance");
                // 检修状态：黄灯以 MaintenanceYellowBlinkIntervalMs 为周期持续闪烁（亮/灭各一次），直到状态切离。
                _ = _safeExecutor.ExecuteAsync(async () => {
                    while (!token.IsCancellationRequested) {
                        // 步骤a：亮黄灯。
                        await _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Yellow).ConfigureAwait(false);
                        try {
                            await Task.Delay(MaintenanceYellowBlinkIntervalMs, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) {
                            // 步骤b：被取消时关灯后退出。
                            _logger.LogDebug("检修黄灯亮灯等待已被新状态取消，执行关灯。");
                            await _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Off).ConfigureAwait(false);
                            return;
                        }
                        // 步骤c：灭黄灯。
                        await _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Off).ConfigureAwait(false);
                        try {
                            await Task.Delay(MaintenanceYellowBlinkIntervalMs, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) {
                            _logger.LogDebug("检修黄灯灭灯等待已被新状态取消。");
                            return;
                        }
                    }
                }, "SignalTower.Maintenance.YellowBlink");
                break;
        }
    }

    /// <summary>
    /// 小车建环完成处理：切换为绿灯。
    /// </summary>
    private void OnRingBuilt() {
        _ = _safeExecutor.ExecuteAsync(
            () => _signalTower.SetLightStatusAsync(SignalTowerLightStatus.Green).AsTask(),
            "SignalTower.SetLight.Green");
    }

    /// <summary>
    /// 取消旧蜂鸣会话并创建新会话，返回新代际号与取消令牌。
    /// </summary>
    private (int gen, CancellationToken token) StartNewBuzzerSession() {
        CancellationTokenSource? old;
        var newCts = new CancellationTokenSource();
        int gen;
        lock (_buzzerLock) {
            old = _buzzerCts;
            _buzzerCts = newCts;
            gen = Interlocked.Increment(ref _buzzerGeneration);
        }
        // 仅发出取消信号，不立即 Dispose：
        // 快速切换状态时，捕获旧 token 的 lambda 可能尚在线程池队列中未执行，
        // 若此时 Dispose 旧 CTS，lambda 内 Task.Delay(x, token) 注册取消回调时会抛出
        // ObjectDisposedException，导致蜂鸣异常无法停止；GC 负责最终回收。
        old?.Cancel();
        return (gen, newCts.Token);
    }

    /// <summary>
    /// 判断当前蜂鸣会话是否仍为最新代际，避免旧任务并发覆盖新状态输出。
    /// </summary>
    /// <param name="gen">会话代际号。</param>
    /// <returns>最新代际返回 true，否则返回 false。</returns>
    private bool IsLatestBuzzerGeneration(int gen) {
        return Volatile.Read(ref _buzzerGeneration) == gen;
    }

    /// <summary>
    /// 取消当前活跃的蜂鸣会话（服务停止时调用）。
    /// </summary>
    private void CancelBuzzer() {
        CancellationTokenSourceHelper.CancelAndDispose(_buzzerLock, ref _buzzerCts);
    }

    /// <summary>
    /// 退订所有已订阅事件。
    /// </summary>
    private void UnsubscribeEvents() {
        if (_stateChangedHandler is not null) {
            _systemStateManager.StateChanged -= _stateChangedHandler;
            _stateChangedHandler = null;
        }
        if (_ringBuiltHandler is not null) {
            _carrierManager.RingBuilt -= _ringBuiltHandler;
            _ringBuiltHandler = null;
        }
        if (_managerChangedHandler is not null) {
            _loopTrackAccessor.ManagerChanged -= _managerChangedHandler;
            _managerChangedHandler = null;
        }
        if (_subscribedManager is not null && _stabilizationStatusChangedHandler is not null) {
            _subscribedManager.StabilizationStatusChanged -= _stabilizationStatusChangedHandler;
            _stabilizationStatusChangedHandler = null;
            _subscribedManager = null;
        }
    }
}
