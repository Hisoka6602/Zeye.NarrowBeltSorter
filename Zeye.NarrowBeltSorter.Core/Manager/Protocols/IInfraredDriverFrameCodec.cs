using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Zeye.NarrowBeltSorter.Core.Options.Chutes;

namespace Zeye.NarrowBeltSorter.Core.Manager.Protocols {

    /// <summary>
    /// 红外驱动器报文编解码器
    /// </summary>
    public interface IInfraredDriverFrameCodec {

        /// <summary>
        /// 厂商编码
        /// </summary>
        string VendorCode { get; }

        /// <summary>
        /// 编码为红外驱动器报文
        /// </summary>
        /// <param name="request">命令请求</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>编码结果</returns>
        ValueTask<(bool, ReadOnlyMemory<byte>)> EncodeAsync(
            InfraredChuteOptions request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 解析红外驱动器报文
        /// </summary>
        /// <param name="frame">原始报文</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>解析结果</returns>
        ValueTask<(bool, InfraredChuteOptions)> DecodeAsync(
            ReadOnlyMemory<byte> frame,
            CancellationToken cancellationToken = default);
    }
}
