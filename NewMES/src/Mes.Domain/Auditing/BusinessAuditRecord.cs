using Mes.Domain.Identity;

namespace Mes.Domain.Auditing;

public sealed class BusinessAuditRecord
{
    public Guid Id { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }

    public Guid? ActorUserId { get; init; }

    public required string ActorUsername { get; init; }

    public BusinessRole? AuthorizedRole { get; init; }

    public BusinessCapability? Capability { get; init; }

    public required string Action { get; init; }

    public required string BusinessObjectType { get; init; }

    public required string BusinessObjectId { get; init; }

    public BusinessAuditResult Result { get; init; }

    public string? ReasonCode { get; init; }

    public required string CorrelationId { get; init; }

    public UserAccount? ActorUser { get; init; }
}
