using System;
using Microsoft.Extensions.Logging;
using Zeye.NarrowBeltSorter.Core.Enums.Parcel;

namespace Zeye.NarrowBeltSorter.Execution.Parcel {

    /// <summary>
    /// 包裹管理器高性能结构化日志定义（源生成，零分配格式化）。
    /// </summary>
    internal static partial class ParcelManagerLog {

        /// <summary>包裹创建日志。</summary>
        [LoggerMessage(EventId = 2300, Level = LogLevel.Information,
            Message = "包裹创建：ParcelId={ParcelId} BarCode={BarCode} CreatedAt={CreatedAt:o}")]
        public static partial void Created(ILogger logger, long parcelId, string barCode, DateTime createdAt);

        /// <summary>目标格口更新日志。</summary>
        [LoggerMessage(EventId = 2310, Level = LogLevel.Information,
            Message = "目标格口更新：ParcelId={ParcelId} OldTargetChuteId={OldTargetChuteId} NewTargetChuteId={NewTargetChuteId} AssignedAt={AssignedAt:o}")]
        public static partial void TargetUpdated(ILogger logger, long parcelId, long oldTargetChuteId, long newTargetChuteId, DateTime assignedAt);

        /// <summary>包裹小车更新日志。</summary>
        [LoggerMessage(EventId = 2320, Level = LogLevel.Information,
            Message = "包裹小车更新：ParcelId={ParcelId} ChangeType={ChangeType} CarrierId={CarrierId} UpdatedAt={UpdatedAt:o} CarrierCount={CarrierCount}")]
        public static partial void CarriersUpdated(ILogger logger, long parcelId, ParcelCarriersChangeType changeType, long? carrierId, DateTime updatedAt, int carrierCount);

        /// <summary>包裹落格日志。</summary>
        [LoggerMessage(EventId = 2330, Level = LogLevel.Information,
            Message = "包裹落格：ParcelId={ParcelId} ActualChuteId={ActualChuteId} CurrentInductionCarrierId={CurrentInductionCarrierId} DroppedAt={DroppedAt:o}")]
        public static partial void Dropped(ILogger logger, long parcelId, long actualChuteId, long? currentInductionCarrierId, DateTime droppedAt);

        /// <summary>包裹移除日志。</summary>
        [LoggerMessage(EventId = 2340, Level = LogLevel.Information,
            Message = "包裹移除：ParcelId={ParcelId} Reason={Reason} RemovedAt={RemovedAt:o}")]
        public static partial void Removed(ILogger logger, long parcelId, string? reason, DateTime removedAt);

        /// <summary>包裹清空日志。</summary>
        [LoggerMessage(EventId = 2350, Level = LogLevel.Information,
            Message = "包裹清空：Reason={Reason} CountBefore={CountBefore} ClearedAt={ClearedAt:o}")]
        public static partial void Cleared(ILogger logger, string? reason, int countBefore, DateTime clearedAt);

        /// <summary>操作被拒绝日志。</summary>
        [LoggerMessage(EventId = 2390, Level = LogLevel.Trace,
            Message = "操作被拒绝：Operation={Operation} ParcelId={ParcelId}")]
        public static partial void Rejected(ILogger logger, string operation, long parcelId);

        /// <summary>包裹管理器异常日志。</summary>
        [LoggerMessage(EventId = 2399, Level = LogLevel.Error,
            Message = "包裹管理器异常：Message={Message}")]
        public static partial void Faulted(ILogger logger, string message, Exception exception);
    }
}
