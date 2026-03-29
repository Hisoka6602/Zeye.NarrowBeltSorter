namespace Zeye.NarrowBeltSorter.Core.Manager.InductionLane {

    /// <summary>
    /// 供包通道接口（描述单路供包通道状态与控制能力）
    /// </summary>
    public interface IInductionLane {
        /// <summary>
        /// 供包通道 Id
        /// </summary>
        long Id { get; }

        /// <summary>
        /// 供包通道名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 当前是否启用
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// 设置供包通道启用状态（设置失败或状态不允许变更时返回 false）
        /// </summary>
        ValueTask<bool> SetEnabledAsync(
            bool isEnabled,
            CancellationToken cancellationToken = default);
    }
}
