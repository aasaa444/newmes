using Mes.Domain.Identity;

namespace Mes.Domain.Auditing;

// 权限与业务命令审计，不等同于制造事件；它记录授权角色、能力和拒绝原因。
public sealed class BusinessAuditRecord
{
    public Guid Id { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }

    public Guid? ActorUserId { get; init; }

    public required string ActorUsername { get; init; }

    public required string ActorRolesSnapshot { get; init; }

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
