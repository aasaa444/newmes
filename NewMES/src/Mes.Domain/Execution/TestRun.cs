namespace Mes.Domain.Execution;

public sealed class TestRun
{
    public Guid Id { get; init; }

    public Guid ProductIdentityId { get; init; }

    public ProductIdentity? ProductIdentity { get; init; }

    public Guid ProductionOrderId { get; init; }

    public Guid ExecutionSnapshotId { get; init; }

    public required string SpecificationCode { get; init; }

    public required string SpecificationVersion { get; init; }

    public required string SpecificationDefinitionHash { get; init; }

    public required string OperationCode { get; init; }

    public required string DeviceId { get; init; }

    public required string DeviceVersion { get; init; }

    public required string FixtureId { get; init; }

    public required string FixtureVersion { get; init; }

    public required string RawReportReference { get; init; }

    public TestRunResult Result { get; init; }

    public string? DiagnosticCode { get; init; }

    public string? DiagnosticMessage { get; init; }

    public Guid? RetryOfTestRunId { get; init; }

    public TestRun? RetryOfTestRun { get; init; }

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

    public bool OperationCompleted { get; init; }

    public string? NextOperationCodeAfter { get; init; }

    public ICollection<TestMeasurement> Measurements { get; } = [];
}
