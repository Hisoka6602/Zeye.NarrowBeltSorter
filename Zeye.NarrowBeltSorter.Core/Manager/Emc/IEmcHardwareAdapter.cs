namespace Zeye.NarrowBeltSorter.Core.Manager.Emc {
    /// <summary>
    /// EMC 硬件访问适配器抽象。
    /// </summary>
    public interface IEmcHardwareAdapter {
        /// <summary>
        /// 初始化控制卡。
        /// </summary>
        /// <param name="cardNo">板卡序号。</param>
        /// <param name="controllerIp">控制器 IP；为空时按本地板卡模式初始化。</param>
        /// <returns>返回码（0 表示成功）。</returns>
        short InitializeBoard(ushort cardNo, string? controllerIp);

        /// <summary>
        /// 对控制卡执行软复位。
        /// </summary>
        /// <param name="cardNo">板卡序号。</param>
        /// <returns>返回码（0 表示成功）。</returns>
        short SoftReset(ushort cardNo);

        /// <summary>
        /// 读取驱动错误码。
        /// </summary>
        /// <param name="cardNo">板卡序号。</param>
        /// <param name="channel">通道号。</param>
        /// <param name="errorCode">输出错误码。</param>
        /// <returns>返回码（0 表示成功）。</returns>
        short GetErrorCode(ushort cardNo, ushort channel, ref ushort errorCode);

        /// <summary>
        /// 读取输入端口位图。
        /// </summary>
        /// <param name="cardNo">板卡序号。</param>
        /// <param name="portNo">端口号。</param>
        /// <returns>端口位图。</returns>
        uint ReadInPort(ushort cardNo, ushort portNo);

        /// <summary>
        /// 写输出位。
        /// </summary>
        /// <param name="cardNo">板卡序号。</param>
        /// <param name="bitNo">位号。</param>
        /// <param name="onOff">值（0/1）。</param>
        /// <returns>返回码（0 表示成功）。</returns>
        short WriteOutBit(ushort cardNo, ushort bitNo, ushort onOff);

        /// <summary>
        /// 关闭控制卡。
        /// </summary>
        /// <returns>返回码（0 表示成功）。</returns>
        short CloseBoard();
    }
}
