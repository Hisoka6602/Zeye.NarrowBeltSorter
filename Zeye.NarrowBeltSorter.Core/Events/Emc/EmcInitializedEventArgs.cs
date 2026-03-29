namespace Zeye.NarrowBeltSorter.Core.Events.Emc {
    /// <summary>
    /// EMC 初始化完成事件载荷。
    /// </summary>
    /// <param name="InitializedAtLocal">本地时间语义的初始化完成时间。</param>
    public readonly record struct EmcInitializedEventArgs(DateTime InitializedAtLocal);
}
