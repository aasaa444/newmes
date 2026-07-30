namespace Mes.Infrastructure.Quality;

/// <summary>操作工在当前工位报告已观察到的异常；处置意见不属于该命令。</summary>
public sealed record NonconformanceReportRequest(
    string? SourceSystem,
    string? IdempotencyKey,
    string? FinishedSerialNumber,
    string? DetectedOperationCode,
    string? DefectCode,
    string? Phenomenon,
    string? EvidenceReference,
    DateTimeOffset? DetectedAtUtc,
    string? Location);

/// <summary>返回本次不合格事实及其关联的独立质量保留，供工位明确知道产品已被隔离。</summary>
public sealed record NonconformanceReportResult(
    Guid NonconformanceId,
    string ReferenceNumber,
    string Status,
    Guid ProductIdentityId,
    string FinishedSerialNumber,
    Guid ProductionOrderId,
    Guid QualityHoldId,
    string HoldStatus,
    Guid ManufacturingEventId,
    bool IsReplay);

// 质量工作台返回不合格证据及其独立保留状态，不混入尚未发生的处置决定。
public sealed record NonconformanceWorkbenchItem(
    Guid NonconformanceId,
    string ReferenceNumber,
    string Status,
    Guid ProductIdentityId,
    string FinishedSerialNumber,
    Guid ProductionOrderId,
    string ProductionOrderNumber,
    string DetectedOperationCode,
    string DefectCode,
    string Phenomenon,
    string EvidenceReference,
    Guid? RelatedTestRunId,
    Guid QualityHoldId,
    string HoldStatus,
    string HoldStartReasonCode,
    string HoldStartReason,
    string HoldReleaseCondition,
    DateTimeOffset HoldStartedAtUtc,
    DateTimeOffset DetectedAtUtc,
    string ReportedByUsername,
    string Location,
    string CorrelationId);

public sealed record NonconformanceWorkbenchResult(
    IReadOnlyList<NonconformanceWorkbenchItem> Items);

public sealed class NonconformanceRejectedException(
    string code,
    string message,
    int httpStatusCode) : Exception(message)
{
    public string Code { get; } = code;

    public int HttpStatusCode { get; } = httpStatusCode;
}
