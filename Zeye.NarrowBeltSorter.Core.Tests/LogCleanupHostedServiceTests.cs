using Microsoft.Extensions.Logging.Abstractions;
using Zeye.NarrowBeltSorter.Core.Options.LogCleanup;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Execution.Services;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// LogCleanupHostedService 日志清理测试。
    /// </summary>
    public sealed class LogCleanupHostedServiceTests {
        /// <summary>
        /// 子目录中的过期日志文件应被清理，未过期日志应保留。
        /// </summary>
        [Fact]
        public async Task CleanupOldLogsAsync_WhenLogsInChildDirectory_ShouldDeleteExpiredLogsOnly() {
            // 步骤1：构造测试日志目录与过期/未过期日志样本。
            var logRootDirectory = Path.Combine(Path.GetTempPath(), $"log-cleanup-tests-{Guid.NewGuid():N}");
            var categoryDirectory = Path.Combine(logRootDirectory, "system-status");
            Directory.CreateDirectory(categoryDirectory);
            var expiredLogFilePath = Path.Combine(categoryDirectory, "expired.log");
            var freshLogFilePath = Path.Combine(categoryDirectory, "fresh.log");
            await File.WriteAllTextAsync(expiredLogFilePath, "expired");
            await File.WriteAllTextAsync(freshLogFilePath, "fresh");
            var now = DateTime.Now;
            File.SetLastWriteTime(expiredLogFilePath, now.AddDays(-10));
            File.SetLastWriteTime(freshLogFilePath, now);

            try {
                // 步骤2：执行一次日志清理。
                var service = CreateService(logRootDirectory);
                await service.CleanupOldLogsAsync(CancellationToken.None);

                // 步骤3：断言仅过期日志被删除。
                Assert.False(File.Exists(expiredLogFilePath));
                Assert.True(File.Exists(freshLogFilePath));
            }
            finally {
                if (Directory.Exists(logRootDirectory)) {
                    Directory.Delete(logRootDirectory, recursive: true);
                }
            }
        }

        /// <summary>
        /// 创建日志清理服务测试实例。
        /// </summary>
        /// <param name="logDirectory">日志目录。</param>
        /// <returns>测试服务实例。</returns>
        private static LogCleanupHostedService CreateService(string logDirectory) {
            return new LogCleanupHostedService(
                NullLogger<LogCleanupHostedService>.Instance,
                new SafeExecutor(NullLogger<SafeExecutor>.Instance),
                OptionsMonitorTestHelper.Create(new LogCleanupSettings {
                    Enabled = true,
                    RetentionDays = 2,
                    CheckIntervalHours = 1,
                    LogDirectory = logDirectory
                }));
        }

    }
}
