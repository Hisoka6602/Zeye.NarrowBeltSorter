namespace Zeye.NarrowBeltSorter.Core.Events.Emc {
    /// <summary>
    /// EMC 故障事件载荷。
    /// </summary>
    /// <param name="FaultCode">故障码。</param>
    /// <param name="Message">故障信息。</param>
    /// <param name="OccurredAtLocal">本地时间语义的故障时间。</param>
    public readonly record struct EmcFaultedEventArgs(
        string FaultCode,
        string Message,
        DateTime OccurredAtLocal);
}
