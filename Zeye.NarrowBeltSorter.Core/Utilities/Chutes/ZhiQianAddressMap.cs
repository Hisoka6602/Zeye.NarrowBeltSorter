namespace Zeye.NarrowBeltSorter.Core.Utilities.Chutes {

    /// <summary>
    /// 智嵌 32 路继电器静态地址映射工具。
    /// 统一管理 Y 路（1~32）与 Modbus 线圈地址之间的换算，避免重复代码。
    /// </summary>
    public static class ZhiQianAddressMap {

        /// <summary>
        /// Y 路编号起始值（Y 路从 1 开始）。
        /// </summary>
        public const int DoIndexMin = 1;

        /// <summary>
        /// Y 路编号最大值（最多 32 路）。
        /// </summary>
        public const int DoIndexMax = 32;

        /// <summary>
        /// DO 线圈总路数。
        /// </summary>
        public const int DoChannelCount = 32;

        /// <summary>
        /// 将 Y 路编号（1~32）转换为 Modbus 线圈起始地址（0-based）。
        /// 按手册第 7.4 节：线圈地址从 0x0000 开始，Y1=0，Y2=1，依此类推。
        /// </summary>
        /// <param name="doIndex">Y 路编号（1~32）。</param>
        /// <returns>对应的 Modbus 线圈地址（0-based）。</returns>
        /// <exception cref="ArgumentOutOfRangeException">doIndex 不在 1~32 范围时抛出。</exception>
        public static ushort ToCoilAddress(int doIndex) {
            if (doIndex < DoIndexMin || doIndex > DoIndexMax) {
                throw new ArgumentOutOfRangeException(nameof(doIndex), $"DO 索引必须在 {DoIndexMin}~{DoIndexMax} 范围，当前值：{doIndex}。");
            }

            return (ushort)(doIndex - DoIndexMin);
        }

        /// <summary>
        /// 将 Modbus 线圈地址（0-based）转换为 Y 路编号（1~32）。
        /// </summary>
        /// <param name="coilAddress">线圈地址（0~31）。</param>
        /// <returns>对应的 Y 路编号（1~32）。</returns>
        /// <exception cref="ArgumentOutOfRangeException">coilAddress 超出范围时抛出。</exception>
        public static int ToDoIndex(ushort coilAddress) {
            if (coilAddress >= DoChannelCount) {
                throw new ArgumentOutOfRangeException(nameof(coilAddress), $"线圈地址必须在 0~{DoChannelCount - 1} 范围，当前值：{coilAddress}。");
            }

            return coilAddress + DoIndexMin;
        }

        /// <summary>
        /// 验证 Y 路编号是否在有效范围（1~32）内。
        /// </summary>
        /// <param name="doIndex">Y 路编号。</param>
        /// <returns>是否合法。</returns>
        public static bool IsValidDoIndex(int doIndex) => doIndex >= DoIndexMin && doIndex <= DoIndexMax;
    }
}
