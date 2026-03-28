using NLog;
using System.Diagnostics.CodeAnalysis;
using Zeye.NarrowBeltSorter.Core.Enums.Device;
using Zeye.NarrowBeltSorter.Core.Events.Chutes;
using Zeye.NarrowBeltSorter.Core.Manager.Chutes;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.ZhiQian {

    /// <summary>
    /// 智嵌格口复合管理器（聚合多台继电器设备，统一对外暴露 IChuteManager 接口）。
    /// 每台设备由独立 ZhiQianChuteManager 管理，每台设备有各自独立的 TCP 连接通道，
    /// 通道内指令严格串行（由 ZhiQianBinaryClientAdapter._requestGate 保证）。
    /// 本类负责路由与约束：
    ///   - 操作路由：按 chuteId 找到对应设备管理器，再委托执行。
    ///   - 强排唯一约束：同一时刻全局只允许一个强排格口（跨设备强制互斥）。
    ///   - 连接聚合：全部设备已连接时返回 Connected，任一故障返回 Faulted。
    /// </summary>
    public sealed class ZhiQianCompositeChuteManager : IChuteManager {
        private static readonly Logger Log = LogManager.GetLogger(nameof(ZhiQianCompositeChuteManager));

        private readonly IReadOnlyList<ZhiQianChuteManager> _managers;
        // 路由表：格口 Id → 所属设备管理器（构造时构建，运行期只读）。
        private readonly IReadOnlyDictionary<long, ZhiQianChuteManager> _chuteRouter;
        private bool _disposed;

        /// <inheritdoc />
        public event EventHandler<ChuteParcelDroppedEventArgs>? ParcelDropped;

        /// <inheritdoc />
        public event EventHandler<ForcedChuteChangedEventArgs>? ForcedChuteChanged;

        /// <inheritdoc />
        public event EventHandler<ChuteConfigurationChangedEventArgs>? ChuteConfigurationChanged;

        /// <inheritdoc />
        public event EventHandler<ChuteLockStatusChangedEventArgs>? ChuteLockStatusChanged;

        /// <inheritdoc />
        public event EventHandler<ChuteManagerConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

        /// <inheritdoc />
        public event EventHandler<ChuteManagerFaultedEventArgs>? Faulted;

        /// <summary>
        /// 初始化智嵌格口复合管理器。
        /// </summary>
        /// <param name="managers">各设备管理器列表（至少含两台，单台应直接使用 ZhiQianChuteManager）。</param>
        public ZhiQianCompositeChuteManager(IReadOnlyList<ZhiQianChuteManager> managers) {
            // 步骤1：校验管理器列表非空。
            if (managers == null || managers.Count == 0) {
                throw new ArgumentException("managers 不能为空。", nameof(managers));
            }

            _managers = managers;

            // 步骤2：构建路由表（格口 Id → 所属管理器），校验唯一性。
            var router = new Dictionary<long, ZhiQianChuteManager>();
            foreach (var manager in managers) {
                foreach (var chute in manager.Chutes) {
                    if (!router.TryAdd(chute.Id, manager)) {
                        Log.Error("ZhiQian复合管理器检测到重复格口 chuteId={0}，已拒绝第二个绑定", chute.Id);
                        throw new ArgumentException($"格口 chuteId={chute.Id} 在多台设备中重复绑定，每个格口只能对应一台设备。", nameof(managers));
                    }
                }
            }

            _chuteRouter = router;

            // 步骤3：透传所有设备管理器的事件。
            foreach (var manager in managers) {
                manager.ParcelDropped += (_, args) => ParcelDropped?.Invoke(this, args);
                manager.ForcedChuteChanged += (_, args) => ForcedChuteChanged?.Invoke(this, args);
                manager.ChuteConfigurationChanged += (_, args) => ChuteConfigurationChanged?.Invoke(this, args);
                manager.ChuteLockStatusChanged += (_, args) => ChuteLockStatusChanged?.Invoke(this, args);
                manager.ConnectionStatusChanged += (_, args) => ConnectionStatusChanged?.Invoke(this, args);
                manager.Faulted += (_, args) => Faulted?.Invoke(this, args);
            }
        }

        /// <inheritdoc />
        public IReadOnlyCollection<IChute> Chutes =>
            _managers.SelectMany(m => m.Chutes).ToList();

        /// <inheritdoc />
        public long? ForcedChuteId =>
            _managers.Select(m => m.ForcedChuteId).FirstOrDefault(id => id.HasValue);

        /// <inheritdoc />
        public IReadOnlySet<long> TargetChuteIds =>
            _managers.SelectMany(m => m.TargetChuteIds).ToHashSet();

        /// <inheritdoc />
        public IReadOnlyDictionary<long, string> ChuteConfigurationSnapshot =>
            _managers.SelectMany(m => m.ChuteConfigurationSnapshot)
                     .ToDictionary(kv => kv.Key, kv => kv.Value);

        /// <inheritdoc />
        public IReadOnlySet<long> LockedChuteIds =>
            _managers.SelectMany(m => m.LockedChuteIds).ToHashSet();

        /// <inheritdoc />
        public DeviceConnectionStatus ConnectionStatus {
            get {
                var statuses = _managers.Select(m => m.ConnectionStatus).ToList();
                if (statuses.Any(s => s == DeviceConnectionStatus.Faulted)) {
                    return DeviceConnectionStatus.Faulted;
                }

                if (statuses.Any(s => s == DeviceConnectionStatus.Disconnected)) {
                    return DeviceConnectionStatus.Disconnected;
                }

                if (statuses.Any(s => s == DeviceConnectionStatus.Connecting)) {
                    return DeviceConnectionStatus.Connecting;
                }

                return DeviceConnectionStatus.Connected;
            }
        }

        /// <inheritdoc />
        public async ValueTask<bool> ConnectAsync(CancellationToken cancellationToken = default) {
            // 各设备独立连接，可并行（各设备有各自独立的连接通道）。
            var results = await Task.WhenAll(
                _managers.Select(m => m.ConnectAsync(cancellationToken).AsTask()))
                .ConfigureAwait(false);
            return results.All(r => r);
        }

        /// <inheritdoc />
        public async ValueTask<bool> DisconnectAsync(CancellationToken cancellationToken = default) {
            // 各设备独立断开，可并行。
            var results = await Task.WhenAll(
                _managers.Select(m => m.DisconnectAsync(cancellationToken).AsTask()))
                .ConfigureAwait(false);
            return results.All(r => r);
        }

        /// <inheritdoc />
        public async ValueTask<bool> SetForcedChuteAsync(long? chuteId, CancellationToken cancellationToken = default) {
            if (chuteId.HasValue) {
                if (!_chuteRouter.TryGetValue(chuteId.Value, out var targetManager)) {
                    Log.Error("ZhiQian复合管理器强排格口不在任何设备映射中 chuteId={0}", chuteId.Value);
                    return false;
                }

                // 步骤1：先清除其他所有设备的强排（全局唯一约束）。
                foreach (var m in _managers.Where(m => !ReferenceEquals(m, targetManager))) {
                    await m.SetForcedChuteAsync(null, cancellationToken).ConfigureAwait(false);
                }

                // 步骤2：在目标设备上设置强排。
                return await targetManager.SetForcedChuteAsync(chuteId, cancellationToken).ConfigureAwait(false);
            }

            // 清空全局强排（向所有设备广播 null）。
            var allOk = true;
            foreach (var m in _managers) {
                if (!await m.SetForcedChuteAsync(null, cancellationToken).ConfigureAwait(false)) {
                    allOk = false;
                }
            }

            return allOk;
        }

        /// <inheritdoc />
        public ValueTask<bool> AddTargetChuteAsync(long chuteId, CancellationToken cancellationToken = default) {
            if (!_chuteRouter.TryGetValue(chuteId, out var manager)) {
                Log.Error("ZhiQian复合管理器添加目标格口不在任何设备映射中 chuteId={0}", chuteId);
                return ValueTask.FromResult(false);
            }

            return manager.AddTargetChuteAsync(chuteId, cancellationToken);
        }

        /// <inheritdoc />
        public ValueTask<bool> RemoveTargetChuteAsync(long chuteId, CancellationToken cancellationToken = default) {
            if (!_chuteRouter.TryGetValue(chuteId, out var manager)) {
                Log.Error("ZhiQian复合管理器移除目标格口不在任何设备映射中 chuteId={0}", chuteId);
                return ValueTask.FromResult(false);
            }

            return manager.RemoveTargetChuteAsync(chuteId, cancellationToken);
        }

        /// <inheritdoc />
        public ValueTask<bool> SetChuteLockedAsync(long chuteId, bool isLocked, CancellationToken cancellationToken = default) {
            if (!_chuteRouter.TryGetValue(chuteId, out var manager)) {
                Log.Error("ZhiQian复合管理器锁格不在任何设备映射中 chuteId={0}", chuteId);
                return ValueTask.FromResult(false);
            }

            return manager.SetChuteLockedAsync(chuteId, isLocked, cancellationToken);
        }

        /// <inheritdoc />
        public bool TryGetChute(long chuteId, [MaybeNullWhen(false)] out IChute chute) {
            chute = null;
            if (!_chuteRouter.TryGetValue(chuteId, out var manager)) {
                return false;
            }

            return manager.TryGetChute(chuteId, out chute);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            foreach (var manager in _managers) {
                try {
                    await manager.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex) {
                    Log.Error(ex, "ZhiQian复合管理器释放子管理器时异常");
                }
            }
        }
    }
}
