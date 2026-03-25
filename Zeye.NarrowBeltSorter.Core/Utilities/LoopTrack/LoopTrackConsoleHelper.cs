using Microsoft.Extensions.Logging;

namespace Zeye.NarrowBeltSorter.Core.Utilities.LoopTrack {
    /// <summary>
    /// 环轨控制台环境检测工具。
    /// </summary>
    public static class LoopTrackConsoleHelper {
        /// <summary>
        /// 判断当前是否为可交互控制台环境。
        /// </summary>
        /// <param name="logger">可选日志组件。</param>
        /// <returns>可交互返回 true，否则返回 false。</returns>
        public static bool IsInteractive(ILogger? logger = null) {
            try {
                return Environment.UserInteractive && !Console.IsInputRedirected;
            }
            catch (InvalidOperationException ex) {
                logger?.LogWarning(ex, "控制台交互能力检测失败：控制台状态不可用，已降级为非交互模式。");
                return false;
            }
            catch (IOException ex) {
                logger?.LogWarning(ex, "控制台交互能力检测失败：输入流访问异常，已降级为非交互模式。");
                return false;
            }
        }
    }
}
