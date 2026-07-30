using Mes.Domain.Execution;
using Mes.Domain.Identity;

namespace Mes.Domain.Quality;

// 对一次已确认偏离的正式记录；它保存发现证据，但不把隔离状态或处置决定塞进测试结果字段。
public sealed class NonconformanceRecord
{
    public Guid Id { get; init; }

    public required string ReferenceNumber { get; init; }

    public Guid ProductIdentityId { get; init; }

    public ProductIdentity? ProductIdentity { get; init; }

    public Guid ProductionOrderId { get; init; }

    public ProductionOrder? ProductionOrder { get; init; }

    public Guid ExecutionSnapshotId { get; init; }

    public ProductionOrderExecutionSnapshot? ExecutionSnapshot { get; init; }

    public Guid QualityHoldId { get; init; }

    public QualityHold? QualityHold { get; init; }

    public NonconformanceStatus Status { get; init; }

    public required string DetectedOperationCode { get; init; }

    public required string DefectCode { get; init; }

    public required string Phenomenon { get; init; }

    public required string EvidenceReference { get; init; }

    public Guid? RelatedTestRunId { get; init; }

    public TestRun? RelatedTestRun { get; init; }

    public required string SourceSystem { get; init; }

    public required string IdempotencyKey { get; init; }

    public required string CommandHash { get; init; }

    public required string CommandHashAlgorithm { get; init; }

    public Guid ReportedByUserId { get; init; }

    public UserAccount? ReportedByUser { get; init; }

    public required string ReportedByUsername { get; init; }

    public DateTimeOffset DetectedAtUtc { get; init; }

    public DateTimeOffset RecordedAtUtc { get; init; }

    public required string Location { get; init; }

    public Guid ManufacturingEventId { get; init; }

    public ManufacturingEvent? ManufacturingEvent { get; init; }

    public required string CorrelationId { get; init; }
}
