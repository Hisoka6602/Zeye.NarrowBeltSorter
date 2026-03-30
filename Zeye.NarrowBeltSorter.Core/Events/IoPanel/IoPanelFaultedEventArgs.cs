namespace Zeye.NarrowBeltSorter.Core.Events.IoPanel {
    /// <summary>
    /// IoPanel 异常事件载荷。
    /// </summary>
    /// <param name="Message">异常描述信息。</param>
    /// <param name="Exception">关联的异常对象（可为空）。</param>
    /// <param name="Timestamp">异常发生时间（本地时间语义，DateTimeKind.Local）。</param>
    public readonly record struct IoPanelFaultedEventArgs(
        string Message,
        Exception? Exception,
        DateTime Timestamp);
}
