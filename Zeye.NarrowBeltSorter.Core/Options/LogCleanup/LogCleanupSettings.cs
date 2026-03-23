namespace Zeye.NarrowBeltSorter.Core.Options.LogCleanup {
    public record class LogCleanupSettings {
        public bool Enabled { get; set; } = true;
        public int RetentionDays { get; set; } = 2;
        public int CheckIntervalHours { get; set; } = 1;
        public string LogDirectory { get; set; } = "logs";
    }
}
