using Zeye.NarrowBeltSorter.Core.Enums.System;

namespace Zeye.NarrowBeltSorter.Core.Events.System {
    /// <summary>
    /// 系统状态变更事件载荷。
    /// </summary>
    /// <param name="OldState">变更前状态。</param>
    /// <param name="NewState">变更后状态。</param>
    /// <param name="ChangedAt">变更时间。</param>
    public readonly record struct StateChangeEventArgs(
        SystemState OldState,
        SystemState NewState,
        DateTime ChangedAt);
}
