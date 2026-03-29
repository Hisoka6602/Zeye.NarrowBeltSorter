using Microsoft.Extensions.Configuration;
using NLog.Config;
using NLog.Targets;
using Zeye.NarrowBeltSorter.Core.Options.LoopTrack;
using Zeye.NarrowBeltSorter.Core.Options.TrackSegment;

namespace Zeye.NarrowBeltSorter.Core.Tests {
    /// <summary>
    /// LoopTrack 日志配置测试。
    /// </summary>
    public sealed class LoopTrackLoggingConfigurationTests {
        /// <summary>
        /// PID 默认值应与 Host 配置保持一致。
        /// </summary>
        [Theory]
        [InlineData("appsettings.json")]
        public void PidDefaults_ShouldMatchHostConfiguration(string appsettingsFileName) {
            // 步骤1：加载 Host 配置文件并绑定 LoopTrack 选项。
            // 步骤2：断言配置绑定后的 PID 参数与 Core 默认值一致。
            var hostProjectPath = GetHostProjectPath();
            var configuration = new ConfigurationBuilder()
                .SetBasePath(hostProjectPath)
                .AddJsonFile(appsettingsFileName, optional: false)
                .Build();
            var serviceOptions = new LoopTrackServiceOptions();
            configuration.GetSection("LoopTrack").Bind(serviceOptions);
            var defaultPid = new LoopTrackPidOptions();

            Assert.Equal(defaultPid.Kp, serviceOptions.Pid.Kp);
            Assert.Equal(defaultPid.Ki, serviceOptions.Pid.Ki);
            Assert.Equal(defaultPid.Kd, serviceOptions.Pid.Kd);
        }

        /// <summary>
        /// 分类日志路由应包含 LoopTrack 关键分类目标。
        /// </summary>
        [Fact]
        public void NLogConfig_ShouldContainRequiredLoopTrackCategoryTargets() {
            // 步骤1：解析 Host 层 NLog.config。
            // 步骤2：校验 LoopTrack 分类 target 与规则均已声明。
            var config = LoadNLogConfiguration();

            Assert.NotNull(config.FindTargetByName<FileTarget>("looptrack-status"));
            Assert.NotNull(config.FindTargetByName<FileTarget>("looptrack-pid"));
            Assert.NotNull(config.FindTargetByName<FileTarget>("looptrack-modbus"));
            Assert.NotNull(config.FindTargetByName<FileTarget>("looptrack-fault"));
            Assert.Contains(config.LoggingRules, rule => rule.Targets.Any(target => target.Name == "looptrack-status"));
            Assert.Contains(config.LoggingRules, rule => rule.Targets.Any(target => target.Name == "looptrack-modbus"));
        }

        /// <summary>
        /// 分类日志路由应包含 chute 关键分类目标。
        /// </summary>
        [Fact]
        public void NLogConfig_ShouldContainRequiredChuteCategoryTargets() {
            // 步骤1：解析 Host 层 NLog.config。
            // 步骤2：校验 chute 分类 target 与规则均已声明。
            var config = LoadNLogConfiguration();

            Assert.NotNull(config.FindTargetByName<FileTarget>("chute-status"));
            Assert.NotNull(config.FindTargetByName<FileTarget>("chute-ZhiQian-Tcp"));
            Assert.NotNull(config.FindTargetByName<FileTarget>("chute-fault"));
            // 旧命名 chute-modbus 已废弃，防止误导实施，配置中不应再存在该 target。
            Assert.Null(config.FindTargetByName<FileTarget>("chute-modbus"));
            Assert.Contains(config.LoggingRules, rule => rule.Targets.Any(target => target.Name == "chute-status"));
            Assert.Contains(config.LoggingRules, rule => rule.Targets.Any(target => target.Name == "chute-ZhiQian-Tcp"));
            Assert.Contains(config.LoggingRules, rule => rule.Targets.Any(target => target.Name == "chute-fault"));
            Assert.DoesNotContain(config.LoggingRules, rule => rule.Targets.Any(target => target.Name == "chute-modbus"));
        }

        /// <summary>
        /// 连接失败日志字段应包含关键根因定位字段。
        /// </summary>
        [Fact]
        public async Task ConnectRetryFailureLog_ShouldContainRequiredContextFields() {
            var entries = new List<string>();
            var logger = new CapturingLogger<Zeye.NarrowBeltSorter.Host.Services.LoopTrackManagerService>(entries);
            var safeExecutor = new Zeye.NarrowBeltSorter.Core.Utilities.SafeExecutor(Microsoft.Extensions.Logging.Abstractions.NullLogger<Zeye.NarrowBeltSorter.Core.Utilities.SafeExecutor>.Instance);
            var service = new TestableLoopTrackManagerService(
                logger,
                safeExecutor,
                Microsoft.Extensions.Options.Options.Create(CreateValidOptions()));

            _ = await service.ExposeExecuteConnectWithRetryPolicyAsync(
                "LoopTrackManagerService.ConnectAsync",
                _ => Task.FromResult((false, false)),
                CancellationToken.None);

            var merged = string.Join(Environment.NewLine, entries);
            Assert.Contains("OperationId=", merged);
            Assert.Contains("Stage=", merged);
            Assert.Contains("SlaveAddresses=", merged);
            Assert.Contains("ExceptionType=", merged);
            Assert.Contains("ExceptionMessage=", merged);
        }

        /// <summary>
        /// 创建默认有效配置。
        /// </summary>
        /// <returns>配置实例。</returns>
        private static LoopTrackServiceOptions CreateValidOptions() {
            return new LoopTrackServiceOptions {
                Enabled = true,
                TrackName = "Test-Track",
                AutoStart = false,
                PollingIntervalMs = 100,
                LeiMaConnection = new LoopTrackLeiMaConnectionOptions {
                    Transport = "SerialRtu",
                    RemoteHost = "127.0.0.1:502",
                    SlaveAddresses = [1, 2],
                    TimeoutMs = 1000,
                    RetryCount = 1,
                    MaxOutputHz = 25m,
                    MaxTorqueRawUnit = 1000,
                    TorqueSetpointWriteIntervalMs = 300
                },
                Pid = new LoopTrackPidOptions()
            };
        }

        /// <summary>
        /// 获取 Host 项目路径。
        /// </summary>
        /// <returns>绝对路径。</returns>
        private static string GetHostProjectPath() {
            var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            return Path.Combine(projectRoot, "Zeye.NarrowBeltSorter.Host");
        }

        /// <summary>
        /// 加载 Host 层 NLog 配置。
        /// </summary>
        /// <returns>NLog 配置对象。</returns>
        private static XmlLoggingConfiguration LoadNLogConfiguration() {
            var hostProjectPath = GetHostProjectPath();
            var nlogConfigPath = Path.Combine(hostProjectPath, "NLog.config");
            return new XmlLoggingConfiguration(nlogConfigPath);
        }
    }
}
