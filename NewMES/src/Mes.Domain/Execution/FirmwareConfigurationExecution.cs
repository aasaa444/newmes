namespace Mes.Domain.Execution;

public sealed class FirmwareConfigurationExecution
{
    public Guid Id { get; init; }

    public Guid ProductIdentityId { get; init; }

    public ProductIdentity? ProductIdentity { get; init; }

    public Guid ProductionOrderId { get; init; }

    public Guid ExecutionSnapshotId { get; init; }

    public required string RequirementCode { get; init; }

    public required string OperationCode { get; init; }

    public required string RequiredVersion { get; init; }

    public required string ActualVersion { get; init; }

    public required string RequiredConfigurationPackage { get; init; }

    public required string ActualConfigurationPackage { get; init; }

    public required string RequiredChecksumAlgorithm { get; init; }

    public required string ActualChecksumAlgorithm { get; init; }

    public required string ExpectedChecksum { get; init; }

    public required string ActualChecksum { get; init; }

    public required string ToolId { get; init; }

    public required string ToolVersion { get; init; }

    public FirmwareExecutionResult Result { get; init; }

    public bool OperationCompleted { get; init; }

    public string? NextOperationCodeAfter { get; init; }

    public string? DiagnosticCode { get; init; }

    public string? DiagnosticMessage { get; init; }

    public string? ReportedDiagnosticCode { get; init; }

    public string? ReportedDiagnosticMessage { get; init; }

    public Guid? RetryOfExecutionId { get; init; }

    public FirmwareConfigurationExecution? RetryOfExecution { get; init; }

    public Guid ManufacturingEventId { get; init; }

    public ManufacturingEvent? ManufacturingEvent { get; init; }

    public required string SourceSystem { get; init; }

    public required string IdempotencyKey { get; init; }

    public required string CommandHash { get; init; }

    public required string CommandHashAlgorithm { get; init; }

    public Guid ActorUserId { get; init; }

    public required string ActorUsername { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset EndedAtUtc { get; init; }

    public DateTimeOffset RecordedAtUtc { get; init; }

    public required string Location { get; init; }

    public required string CorrelationId { get; init; }
}
