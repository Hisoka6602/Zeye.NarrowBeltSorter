using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Options.LogCleanup;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 日志清理服务 - 自动清理超过指定天数的日志文件
    /// </summary>
    public sealed class LogCleanupHostedService : BackgroundService {

        /// <summary>
        /// 日志组件。
        /// </summary>
        private readonly ILogger<LogCleanupHostedService> _logger;

        /// <summary>
        /// 全局安全执行器。
        /// </summary>
        private readonly SafeExecutor _safeExecutor;

        /// <summary>
        /// 日志清理配置。
        /// </summary>
        private readonly IOptionsMonitor<LogCleanupSettings> _settingsMonitor;

        /// <summary>
        /// 初始化日志清理服务。
        /// </summary>
        public LogCleanupHostedService(
            ILogger<LogCleanupHostedService> logger,
            SafeExecutor safeExecutor,
            IOptionsMonitor<LogCleanupSettings> settingsMonitor) {
            _logger = logger;
            _safeExecutor = safeExecutor;
            _settingsMonitor = settingsMonitor;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            var currentSettings = _settingsMonitor.CurrentValue;
            if (!currentSettings.Enabled) {
                _logger.LogInformation("日志清理服务已禁用");
                return;
            }

            _logger.LogInformation("日志清理服务已启动，保留天数: {RetentionDays}天，检查间隔: {CheckIntervalHours}小时",
                currentSettings.RetentionDays, currentSettings.CheckIntervalHours);

            // 首次启动时立即执行一次清理
            await _safeExecutor.ExecuteAsync(
                () => CleanupOldLogsAsync(stoppingToken),
                "首次日志清理");

            while (!stoppingToken.IsCancellationRequested) {
                try {
                    var delaySettings = _settingsMonitor.CurrentValue;
                    await Task.Delay(TimeSpan.FromHours(delaySettings.CheckIntervalHours), stoppingToken);

                    await _safeExecutor.ExecuteAsync(
                        () => CleanupOldLogsAsync(stoppingToken),
                        "定期日志清理");
                }
                catch (OperationCanceledException) {
                    // 服务正在停止，正常退出
                    _logger.LogInformation("日志清理服务正在停止");
                    break;
                }
            }
        }

        /// <summary>
        /// 清理超期日志文件。
        /// </summary>
        private Task CleanupOldLogsAsync(CancellationToken cancellationToken) {
            var settings = _settingsMonitor.CurrentValue;
            var logDirectory = settings.LogDirectory;

            // 如果是相对路径，转换为绝对路径
            if (!Path.IsPathRooted(logDirectory)) {
                logDirectory = Path.Combine(AppContext.BaseDirectory, logDirectory);
            }

            if (!Directory.Exists(logDirectory)) {
                _logger.LogWarning("日志目录不存在: {LogDirectory}", logDirectory);
                return Task.CompletedTask;
            }

            var cutoffDate = DateTime.Now.AddDays(-settings.RetentionDays);
            _logger.LogInformation("开始清理日志，删除 {CutoffDate} 之前的日志文件", cutoffDate);

            var deletedCount = 0;
            var failedCount = 0;
            var scanFailedCount = 0;

            // 步骤1：清理当前日志目录中的超期日志文件。
            // 清理日志目录中的旧文件
            var (deleted1, failed1, scanFailed1) = CleanupDirectory(logDirectory, cutoffDate, cancellationToken);
            deletedCount += deleted1;
            failedCount += failed1;
            scanFailedCount += scanFailed1;

            // 步骤2：如果存在归档目录，则执行同口径清理并汇总统计。
            // 清理归档目录中的旧文件
            var archiveDirectory = Path.Combine(logDirectory, "archives");
            if (Directory.Exists(archiveDirectory)) {
                var (deleted2, failed2, scanFailed2) = CleanupDirectory(archiveDirectory, cutoffDate, cancellationToken);
                deletedCount += deleted2;
                failedCount += failed2;
                scanFailedCount += scanFailed2;
            }

            var totalFailedCount = failedCount + scanFailedCount;
            _logger.LogInformation("日志清理完成，删除文件数: {DeletedCount}，删除失败数: {DeleteFailedCount}，目录扫描失败数: {ScanFailedCount}，失败总数: {TotalFailedCount}",
                deletedCount, failedCount, scanFailedCount, totalFailedCount);

            return Task.CompletedTask;
        }

        /// <summary>
        /// 扫描并删除指定目录中的超期日志文件。
        /// </summary>
        private (int DeletedCount, int FailedCount, int ScanFailedCount) CleanupDirectory(string directory, DateTime cutoffDate, CancellationToken cancellationToken) {
            var deletedCount = 0;
            var failedCount = 0;
            var scanFailedCount = 0;

            try {
                // 步骤1：顺序扫描目录日志文件，逐个执行过期判定与删除。
                foreach (var file in Directory.EnumerateFiles(directory, "*.log", SearchOption.TopDirectoryOnly)) {
                    if (cancellationToken.IsCancellationRequested) {
                        _logger.LogInformation("日志清理收到取消信号，中断目录扫描：{Directory}", directory);
                        return (deletedCount, failedCount, scanFailedCount);
                    }

                    try {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.LastWriteTime < cutoffDate) {
                            _logger.LogInformation("删除旧日志文件: {FileName}, 最后修改时间: {LastWriteTime}",
                                fileInfo.Name, fileInfo.LastWriteTime);

                            fileInfo.Delete();
                            deletedCount++;
                        }
                    }
                    catch (Exception ex) {
                        _logger.LogWarning(ex, "删除日志文件失败: {FileName}", file);
                        failedCount++;
                    }
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "扫描日志目录失败: {Directory}", directory);
                scanFailedCount++;
            }

            return (deletedCount, failedCount, scanFailedCount);
        }
    }
}
