namespace Mes.Domain.Execution;

// 不可变制造事实的通用时间线。发生时间与系统记录时间分开，以保留设备延迟上报证据。
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
