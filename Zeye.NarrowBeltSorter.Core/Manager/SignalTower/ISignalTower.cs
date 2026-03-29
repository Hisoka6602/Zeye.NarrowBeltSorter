namespace Zeye.NarrowBeltSorter.Core.Manager.SignalTower;

/// <summary>
/// 信号塔接口（描述单个信号塔状态与控制能力）
/// </summary>
public interface ISignalTower {
    /// <summary>
    /// 信号塔 Id
    /// </summary>
    long Id { get; }

    /// <summary>
    /// 信号塔名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 当前是否启用
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 设置信号塔启用状态（设置失败或状态不允许变更时返回 false）
    /// </summary>
    ValueTask<bool> SetEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken = default);
}
