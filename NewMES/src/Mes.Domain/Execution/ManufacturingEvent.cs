namespace Mes.Domain.Execution;

public sealed class ManufacturingEvent
{
    public Guid Id { get; init; }

    public required string EventType { get; init; }

    public required string AggregateType { get; init; }

    public required string AggregateId { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }

    public DateTimeOffset RecordedAtUtc { get; init; }

    public required string Actor { get; init; }

    public required string PayloadJson { get; init; }

    public Guid? ProductIdentityId { get; init; }

    public Guid? ProductionOrderId { get; init; }

    public Guid? ExecutionSnapshotId { get; init; }

    public string? Location { get; init; }

    public string? CorrelationId { get; init; }

    public Guid? CausationEventId { get; init; }

    public ManufacturingEvent? CausationEvent { get; init; }

    public Guid? CorrectsEventId { get; init; }

    public ManufacturingEvent? CorrectsEvent { get; init; }
}
