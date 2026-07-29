namespace Mes.Domain.Integration;

public sealed class IntegrationInboxConflict
{
    public Guid Id { get; init; }

    public Guid InboxMessageId { get; init; }

    public IntegrationInboxMessage InboxMessage { get; init; } = null!;

    public required string ExistingPayloadHash { get; init; }

    public required string ObservedPayloadHash { get; init; }

    public required string ResultCode { get; init; }

    public required string ResultMessage { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }

    public required string CorrelationId { get; init; }
}
