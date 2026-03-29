using Zeye.NarrowBeltSorter.Core.Enums.System;

namespace Zeye.NarrowBeltSorter.Core.Events.Emc {
    /// <summary>
    /// EMC 状态变更事件载荷。
    /// </summary>
    /// <param name="PreviousStatus">变更前状态。</param>
    /// <param name="CurrentStatus">变更后状态。</param>
    /// <param name="OccurredAtLocal">本地时间语义的状态变更时间。</param>
    public readonly record struct EmcStatusChangedEventArgs(
        EmcControllerStatus PreviousStatus,
        EmcControllerStatus CurrentStatus,
        DateTime OccurredAtLocal);
}
