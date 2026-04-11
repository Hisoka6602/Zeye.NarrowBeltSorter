using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.System;

namespace Zeye.NarrowBeltSorter.Core.Manager.System {

    /// <summary>
    /// 系统状态管理器（负责系统运行状态的统一流转与通知）
    /// </summary>
    public interface ISystemStateManager : IDisposable {

        /// <summary>
        /// 获取当前系统状态
        /// </summary>
        SystemState CurrentState { get; }

        /// <summary>
        /// 系统状态变更事件
        /// </summary>
        /// <remarks>
        /// 当系统状态成功转换时触发。
        /// 用于通知其他组件（如队列管理器）执行相应的清理或初始化操作。
        /// </remarks>
        event EventHandler<StateChangeEventArgs>? StateChanged;

        /// <summary>
        /// 变更系统状态
        /// </summary>
        /// <param name="targetState">目标状态。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>转换成功返回 true；目标状态与当前状态相同或不允许转换时返回 false。</returns>
        Task<bool> ChangeStateAsync(SystemState targetState, CancellationToken cancellationToken = default);
    }
}
