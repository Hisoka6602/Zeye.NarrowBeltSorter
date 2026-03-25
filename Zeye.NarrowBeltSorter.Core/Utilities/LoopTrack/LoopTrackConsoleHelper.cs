namespace Zeye.NarrowBeltSorter.Core.Utilities.LoopTrack {
    /// <summary>
    /// 环轨控制台环境检测工具。
    /// </summary>
    public static class LoopTrackConsoleHelper {
        /// <summary>
        /// 判断当前是否为可交互控制台环境。
        /// </summary>
        /// <returns>可交互返回 true，否则返回 false。</returns>
        public static bool IsInteractive() {
            try {
                return Environment.UserInteractive && !Console.IsInputRedirected;
            }
            catch (Exception) {
                return false;
            }
        }
    }
}
