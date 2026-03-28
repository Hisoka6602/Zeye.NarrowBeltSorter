using Zeye.NarrowBeltSorter.Core.Enums.Chutes;
using Zeye.NarrowBeltSorter.Core.Enums.Carrier;

namespace Zeye.NarrowBeltSorter.Core.Options.Chutes {
    /// <summary>
    /// 智嵌格口驱动共享配置。
    /// </summary>
    public sealed record ZhiQianChuteOptions {
        public bool Enabled { get; set; } = false;

        public int CommandTimeoutMs { get; set; } = 300;

        public int RetryCount { get; set; } = 2;

        public int RetryDelayMs { get; set; } = 50;

        public int PollIntervalMs { get; set; } = 100;

        public int DefaultOpenDurationMs { get; set; } = 120;

        public bool ForceOpenExclusive { get; set; } = true;

        /// <summary>
        /// 兼容旧配置：单设备 Host（未配置 Devices 时生效）。
        /// </summary>
        public string Host { get; set; } = "192.168.1.253";

        /// <summary>
        /// 兼容旧配置：单设备 Port（未配置 Devices 时生效）。
        /// </summary>
        public int Port { get; set; } = 1030;

        /// <summary>
        /// 兼容旧配置：单设备 DeviceAddress（未配置 Devices 时生效）。
        /// </summary>
        public byte DeviceAddress { get; set; } = 1;

        /// <summary>
        /// 兼容旧配置：单设备 chute 映射（未配置 Devices 时生效）。
        /// </summary>
        public Dictionary<long, int> ChuteToDoMap { get; set; } = new();

        public List<ZhiQianDeviceOptions> Devices { get; set; } = new();

        public ZhiQianLoggingOptions Logging { get; set; } = new();

        /// <summary>
        /// 校验共享配置与设备集合配置是否合法。
        /// </summary>
        /// <returns>校验错误集合。</returns>
        public IReadOnlyList<string> Validate() {
            // 步骤1：先做旧版单设备配置归一化，确保后续校验统一基于 Devices。
            NormalizeLegacySingleDevice();
            var errors = new List<string>();
            // 步骤2：校验共享参数基础边界，提前拦截非法范围。
            if (CommandTimeoutMs < 100) {
                errors.Add($"CommandTimeoutMs 最小值为 100，当前值：{CommandTimeoutMs}。");
            }

            if (RetryCount < 0) {
                errors.Add($"RetryCount 不能小于 0，当前值：{RetryCount}。");
            }

            if (RetryDelayMs < 10) {
                errors.Add($"RetryDelayMs 最小值为 10，当前值：{RetryDelayMs}。");
            }

            if (PollIntervalMs < 50) {
                errors.Add($"PollIntervalMs 最小值为 50，当前值：{PollIntervalMs}。");
            }

            if (DefaultOpenDurationMs < 20) {
                errors.Add($"DefaultOpenDurationMs 最小值为 20，当前值：{DefaultOpenDurationMs}。");
            }

            // 步骤3：校验设备列表存在性与当前版本支持范围。
            if (Devices.Count == 0) {
                errors.Add("Devices 不能为空，至少需要配置一台设备。");
                return errors;
            }

            if (Devices.Count > 1) {
                errors.Add($"当前版本暂不支持多设备，Devices 仅允许 1 台，当前：{Devices.Count}。");
                return errors;
            }

            // 步骤4：逐台校验设备参数，并校验 chuteId 跨设备全局唯一。
            var seenChuteIds = new HashSet<long>();
            for (var i = 0; i < Devices.Count; i++) {
                var device = Devices[i];
                errors.AddRange(device.Validate(i));
                foreach (var chuteId in device.ChuteToDoMap.Keys) {
                    if (!seenChuteIds.Add(chuteId)) {
                        errors.Add($"跨设备 chuteId={chuteId} 重复，要求全局唯一。");
                    }
                }
            }

            return errors;
        }

        /// <summary>
        /// 兼容旧版单设备配置：当 Devices 为空时，将顶层 Host/Port/DeviceAddress/ChuteToDoMap 映射到 Devices[0]。
        /// </summary>
        public void NormalizeLegacySingleDevice() {
            if (Devices.Count > 0) {
                return;
            }

            if (ChuteToDoMap.Count == 0 && string.IsNullOrWhiteSpace(Host)) {
                return;
            }

            Devices.Add(new ZhiQianDeviceOptions {
                Host = Host,
                Port = Port,
                DeviceAddress = DeviceAddress,
                ChuteToDoMap = ChuteToDoMap.ToDictionary(kv => kv.Key, kv => kv.Value),
                InfraredChuteOptionsMap = ChuteToDoMap.ToDictionary(
                    kv => kv.Key,
                    kv => CreateDefaultInfraredChuteOptions(kv.Value))
            });
        }

        /// <summary>
        /// 根据 DO 路号生成默认红外参数模板。
        /// </summary>
        /// <param name="doIndex">DO 路号。</param>
        /// <returns>默认红外参数。</returns>
        private static InfraredChuteOptions CreateDefaultInfraredChuteOptions(int doIndex) {
            var dinChannel = (doIndex - 1) % 4 + 1;
            return new InfraredChuteOptions {
                DinChannel = dinChannel,
                DefaultDirection = CarrierTurnDirection.Left,
                ControlMode = InfraredControlMode.Position,
                DefaultSpeedMmps = 0,
                DefaultDistanceMm = 0,
                AccelerationMmps2 = 0,
                HoldDurationMs = 0,
                TriggerDelayMs = 0,
                RollerDiameterMm = 0,
                DialCode = 0
            };
        }
    }
}
