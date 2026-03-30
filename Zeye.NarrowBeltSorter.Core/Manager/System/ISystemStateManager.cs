using Zeye.NarrowBeltSorter.Core.Enums.System;
using Zeye.NarrowBeltSorter.Core.Events.System;

namespace Zeye.NarrowBeltSorter.Core.Manager.System {

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
        /// <param name="targetState"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<bool> ChangeStateAsync(SystemState targetState, CancellationToken cancellationToken = default);
    }
}
