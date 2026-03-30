using Zeye.NarrowBeltSorter.Core.Manager.Emc;

namespace Zeye.NarrowBeltSorter.Core.Tests.Leadshaine.Emc {
    /// <summary>
    /// Leadshaine EMC 硬件适配器测试桩。
    /// </summary>
    public sealed class FakeLeadshaineEmcHardwareAdapter : IEmcHardwareAdapter {
        /// <summary>
        /// 初始化返回码。
        /// </summary>
        public short InitializeCode { get; set; }

        /// <summary>
        /// 最后一次初始化参数。
        /// </summary>
        public (ushort CardNo, string? ControllerIp)? LastInitializeArgs { get; private set; }

        /// <summary>
        /// 读取错误码返回值。
        /// </summary>
        public short GetErrorCodeResult { get; set; }

        /// <summary>
        /// 读取错误码调用次数。
        /// </summary>
        public int GetErrorCodeCallCount { get; private set; }

        /// <summary>
        /// 输出错误码。
        /// </summary>
        public ushort ErrorCode { get; set; }

        /// <summary>
        /// 写输出返回码。
        /// </summary>
        public short WriteOutBitResult { get; set; }

        /// <summary>
        /// 输入端口值映射。
        /// </summary>
        public Dictionary<(ushort CardNo, ushort PortNo), uint> InPortValues { get; } = [];

        /// <summary>
        /// 最后一次写输出参数。
        /// </summary>
        public (ushort CardNo, ushort BitNo, ushort OnOff)? LastWriteOutBit { get; private set; }

        /// <inheritdoc />
        public short InitializeBoard(ushort cardNo, string? controllerIp) {
            LastInitializeArgs = (cardNo, controllerIp);
            return InitializeCode;
        }

        /// <inheritdoc />
        public short SoftReset(ushort cardNo) {
            return 0;
        }

        /// <inheritdoc />
        public short GetErrorCode(ushort cardNo, ushort channel, ref ushort errorCode) {
            GetErrorCodeCallCount++;
            errorCode = ErrorCode;
            return GetErrorCodeResult;
        }

        /// <inheritdoc />
        public uint ReadInPort(ushort cardNo, ushort portNo) {
            return InPortValues.TryGetValue((cardNo, portNo), out var value) ? value : 0;
        }

        /// <inheritdoc />
        public short WriteOutBit(ushort cardNo, ushort bitNo, ushort onOff) {
            LastWriteOutBit = (cardNo, bitNo, onOff);
            return WriteOutBitResult;
        }

        /// <inheritdoc />
        public short CloseBoard() {
            return 0;
        }
    }
}
