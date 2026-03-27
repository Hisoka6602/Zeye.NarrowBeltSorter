namespace Zeye.NarrowBeltSorter.Core.Options.Chutes {

    /// <summary>
    /// 格口强排轮转后台服务配置。
    /// </summary>
    public sealed record ChuteForcedRotationOptions {

        /// <summary>
        /// 是否启用格口强排轮转后台服务。
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 强排切换间隔（单位：秒，最小值 1，默认 10）。
        /// </summary>
        public int SwitchIntervalSeconds { get; set; } = 10;

        /// <summary>
        /// 轮转强排格口数组（按顺序循环切换）。
        /// </summary>
        public List<long> ChuteSequence { get; set; } = new();
    }
}
