using System.Runtime.CompilerServices;
using Zeye.NarrowBeltSorter.Core.Manager.Parcel;

namespace Zeye.NarrowBeltSorter.Core.Utilities {
    /// <summary>
    /// 包裹条码日志值辅助工具（空白条码统一输出 null）。
    /// </summary>
    public static class ParcelBarCodeLogHelper {
        /// <summary>
        /// 归一化条码文本（空白值统一视为 null）。
        /// </summary>
        /// <param name="barCode">原始条码。</param>
        /// <returns>归一化结果。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string? Normalize(string? barCode) {
            return string.IsNullOrWhiteSpace(barCode) ? null : barCode;
        }

        /// <summary>
        /// 从包裹管理器按包裹编号获取归一化条码。
        /// </summary>
        /// <param name="parcelManager">包裹管理器。</param>
        /// <param name="parcelId">包裹编号。</param>
        /// <returns>归一化条码。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string? TryGet(IParcelManager parcelManager, long parcelId) {
            return parcelManager is not null && parcelManager.TryGet(parcelId, out var parcel)
                ? Normalize(parcel.BarCode)
                : null;
        }
    }
}
