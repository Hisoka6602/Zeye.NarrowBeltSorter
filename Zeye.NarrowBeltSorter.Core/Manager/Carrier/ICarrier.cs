using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Zeye.NarrowBeltSorter.Core.Manager.Carrier {

    /// <summary>
    /// 小车接口（描述单台小车状态与控制能力）。
    /// </summary>
    public interface ICarrier : IDisposable {

        /// <summary>
        /// 获取小车唯一标识。
        /// </summary>
        long Id { get; }

        /// <summary>
        /// 获取小车当前运行速度。
        /// </summary>
        double CurrentSpeed { get; }

        /// <summary>
        /// 获取一个值，指示当前转向是否为左。
        /// </summary>
        bool IsLeftDirection { get; }

        /// <summary>
        /// 获取一个值，指示当前是否处于连接状态。
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 获取一个值，指示当前是否载货。
        /// </summary>
        bool HasPackage { get; }

        /// <summary>
        /// 获取当前包裹信息；未载货时为 <see langword="null"/>。
        /// </summary>
        string? PackageInfo { get; }

        /// <summary>
        /// 获取当前装载并联小车标识集合。
        /// </summary>
        IReadOnlyCollection<long> LoadedParallelCarrierIds { get; }

        /// <summary>
        /// 获取一个值，指示当前是否被装载并联。
        /// </summary>
        bool IsLoadedAsParallelCarrier { get; }

        /// <summary>
        /// 小车载货状态变更事件。
        /// </summary>
        event EventHandler? LoadStateChanged;

        /// <summary>
        /// 小车连接状态变更事件。
        /// </summary>
        event EventHandler? ConnectionStateChanged;

        /// <summary>
        /// 连接小车。
        /// </summary>
        void Connect();

        /// <summary>
        /// 断开小车连接。
        /// </summary>
        void Disconnect();

        /// <summary>
        /// 设置小车转向方向。
        /// </summary>
        /// <param name="isLeftDirection">是否设置为左转向。</param>
        void SetDirection(bool isLeftDirection);

        /// <summary>
        /// 设置小车运行速度。
        /// </summary>
        /// <param name="speed">目标速度。</param>
        void SetSpeed(double speed);

        /// <summary>
        /// 装载包裹。
        /// </summary>
        /// <param name="packageInfo">包裹信息。</param>
        /// <param name="parallelCarrierIds">并联装载小车标识集合。</param>
        void LoadPackage(string packageInfo, IReadOnlyCollection<long> parallelCarrierIds);

        /// <summary>
        /// 卸载包裹。
        /// </summary>
        void UnloadPackage();
    }
}
