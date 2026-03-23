namespace Zeye.NarrowBeltSorter.Core.Events.Io {
    /// <summary>
    /// 传感器异常事件载荷。
    /// </summary>
    /// <param name="Message">异常描述信息。</param>
    /// <param name="Exception">关联的异常对象（可为空）。</param>
    /// <param name="Timestamp">异常发生时间（本地时间语义，DateTimeKind.Local，约定不得写入 UTC 或 Unspecified）。</param>
    public readonly record struct SensorFaultedEventArgs(
        string Message,
        Exception? Exception,
        DateTime Timestamp);
}
