using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeye.NarrowBeltSorter.Core.Utilities;
using Zeye.NarrowBeltSorter.Core.Options.LogCleanup;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    /// <summary>
    /// 日志清理托管服务，自动清理超过配置保留天数的日志文件。
    /// </summary>
    public sealed class LogCleanupHostedService : BackgroundService {
        private const int MinCheckIntervalHours = 1;

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
        private readonly IDisposable _settingsChangedRegistration;
        private LogCleanupSettings _currentSettings;
        private int _invalidIntervalWarningState;

        /// <summary>
        /// 初始化日志清理服务。
        /// </summary>
        public LogCleanupHostedService(
            ILogger<LogCleanupHostedService> logger,
            SafeExecutor safeExecutor,
            IOptionsMonitor<LogCleanupSettings> settingsMonitor) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _safeExecutor = safeExecutor ?? throw new ArgumentNullException(nameof(safeExecutor));
            _settingsMonitor = settingsMonitor ?? throw new ArgumentNullException(nameof(settingsMonitor));
            _currentSettings = _settingsMonitor.CurrentValue ?? throw new InvalidOperationException("LogCleanupSettings 不能为空。");
            _settingsChangedRegistration = _settingsMonitor.OnChange(RefreshSettingsSnapshot) ?? throw new InvalidOperationException("LogCleanupSettings.OnChange 订阅失败。");
        }

        /// <summary>
        /// 当前日志清理配置快照。
        /// </summary>
        private LogCleanupSettings CurrentSettings => Volatile.Read(ref _currentSettings);

        /// <summary>
        /// 托管服务主循环：启动后立即执行一次日志清理，随后按配置间隔周期性执行。
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            var currentSettings = CurrentSettings;
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
                    var delaySettings = CurrentSettings;
                    var safeCheckIntervalHours = GetSafeCheckIntervalHours(delaySettings.CheckIntervalHours);
                    await Task.Delay(TimeSpan.FromHours(safeCheckIntervalHours), stoppingToken);

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
        internal Task CleanupOldLogsAsync(CancellationToken cancellationToken) {
            var settings = CurrentSettings;
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

            // 步骤1：递归清理日志根目录中的超期日志文件（包含所有分类子目录）。
            var (deleted1, failed1, scanFailed1) = CleanupDirectory(logDirectory, cutoffDate, cancellationToken);
            deletedCount += deleted1;
            failedCount += failed1;
            scanFailedCount += scanFailed1;

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
                // 步骤1：构建目录栈并执行深度优先扫描，确保覆盖所有分类日志子目录。
                var directoryStack = new Stack<string>();
                directoryStack.Push(directory);
                while (directoryStack.Count > 0) {
                    var currentDirectory = directoryStack.Pop();
                    if (cancellationToken.IsCancellationRequested) {
                        _logger.LogInformation("日志清理收到取消信号，中断目录扫描：{Directory}", currentDirectory);
                        return (deletedCount, failedCount, scanFailedCount);
                    }
                    try {
                        // 步骤2：先扫描当前目录下日志文件，再收集子目录继续处理。
                        foreach (var file in Directory.EnumerateFiles(currentDirectory, "*.log", SearchOption.TopDirectoryOnly)) {
                            if (cancellationToken.IsCancellationRequested) {
                                _logger.LogInformation("日志清理收到取消信号，中断目录扫描：{Directory}", currentDirectory);
                                return (deletedCount, failedCount, scanFailedCount);
                            }

                            try {
                                var fileInfo = new FileInfo(file);
                                if (fileInfo.LastWriteTime < cutoffDate) {
                                    _logger.LogDebug("删除旧日志文件: {FileName}, 最后修改时间: {LastWriteTime}",
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

                        foreach (var subDirectory in Directory.EnumerateDirectories(currentDirectory)) {
                            directoryStack.Push(subDirectory);
                        }
                    }
                    catch (Exception ex) {
                        // 步骤3：目录扫描异常按目录级别记录并继续后续目录，避免单点失败中断全局清理。
                        _logger.LogError(ex, "扫描或处理日志目录失败: {Directory}", currentDirectory);
                        scanFailedCount++;
                    }
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "扫描日志目录失败: {Directory}", directory);
                scanFailedCount++;
            }

            return (deletedCount, failedCount, scanFailedCount);
        }

        /// <summary>
        /// 刷新日志清理配置快照。
        /// </summary>
        /// <param name="settings">最新配置。</param>
        private void RefreshSettingsSnapshot(LogCleanupSettings settings) {
            Volatile.Write(ref _currentSettings, settings);
            if (settings.CheckIntervalHours >= MinCheckIntervalHours) {
                Interlocked.Exchange(ref _invalidIntervalWarningState, 0);
            }
        }

        /// <summary>
        /// 获取安全的日志清理检查间隔小时值。
        /// </summary>
        /// <param name="configuredHours">配置值（小时）。</param>
        /// <returns>用于延迟等待的安全小时值。</returns>
        private int GetSafeCheckIntervalHours(int configuredHours) {
            // 步骤1：配置值合法时直接返回。
            if (configuredHours >= MinCheckIntervalHours) {
                return configuredHours;
            }

            // 步骤2：配置值非法时按限频策略输出一次告警。
            // 步骤2：配置值非法时按限频策略输出一次告警（配置恢复合法后标志位会在 RefreshSettingsSnapshot 中重置）。
            if (Interlocked.Exchange(ref _invalidIntervalWarningState, 1) == 0) {
                _logger.LogWarning(
                    "日志清理检查间隔配置无效，已回退为最小值 {MinIntervalHours} 小时。配置值={ConfiguredHours}",
                    MinCheckIntervalHours,
                    configuredHours);
            }

            // 步骤3：回退到最小检查间隔，避免 Task.Delay 异常中断循环。
            return MinCheckIntervalHours;
        }

        /// <summary>
        /// 释放配置热更新订阅资源。
        /// </summary>
        public override void Dispose() {
            _settingsChangedRegistration.Dispose();
            base.Dispose();
        }
    }
}
