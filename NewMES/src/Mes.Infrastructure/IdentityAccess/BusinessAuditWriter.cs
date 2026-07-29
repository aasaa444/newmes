using Mes.Domain.Auditing;
using Mes.Domain.Identity;
using Mes.Infrastructure.Persistence;

namespace Mes.Infrastructure.IdentityAccess;

internal sealed record BusinessAuditActor(
    Guid? UserId,
    string Username,
    IReadOnlyCollection<BusinessRole> Roles)
{
    public static BusinessAuditActor From(EffectiveIdentity identity) =>
        new(identity.UserId, identity.Username, identity.Roles);
}

internal sealed record BusinessAuditWrite(
    BusinessAuditActor Actor,
    BusinessRole? AuthorizedRole,
    BusinessCapability? Capability,
    string Action,
    string ObjectType,
    string ObjectId,
    BusinessAuditResult Result,
    string? ReasonCode,
    string CorrelationId);

internal sealed class BusinessAuditWriter(
    MesDbContext context,
    TimeProvider timeProvider)
{
    public void Append(BusinessAuditWrite write)
    {
        context.BusinessAuditRecords.Add(new BusinessAuditRecord
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = timeProvider.GetUtcNow(),
            ActorUserId = write.Actor.UserId,
            ActorUsername = write.Actor.Username,
            ActorRolesSnapshot = AuditRoleSnapshot.From(write.Actor.Roles),
            AuthorizedRole = write.AuthorizedRole,
            Capability = write.Capability,
            Action = write.Action,
            BusinessObjectType = write.ObjectType,
            BusinessObjectId = write.ObjectId,
            Result = write.Result,
            ReasonCode = write.ReasonCode,
            CorrelationId = write.CorrelationId,
        });
    }
}
