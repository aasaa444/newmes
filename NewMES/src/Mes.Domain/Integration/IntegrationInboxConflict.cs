namespace Mes.Domain.Integration;

// 同一消息标识收到不同载荷时追加的冲突证据，不覆盖第一次已经确认的收件结果。
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
