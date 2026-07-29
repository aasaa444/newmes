using Mes.Domain.Auditing;
using Mes.Domain.Identity;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.IdentityAccess;

public sealed class IdentityAccessService(
    MesDbContext context,
    TimeProvider timeProvider)
{
    private readonly BusinessAuditWriter auditWriter = new(context, timeProvider);

    public async Task<EffectiveIdentity> GetRequiredIdentityAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var account = await context.UserAccounts
            .AsNoTracking()
            .Include(user => user.RoleAssignments)
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("The user account does not exist.");
        return EffectiveIdentityProjector.From(account);
    }

    public async Task ChangeRolesAsync(
        EffectiveIdentity actor,
        Guid targetUserId,
        BusinessRole primaryRole,
        IReadOnlyCollection<BusinessRole> roles,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var grant = await DemandAsync(
            actor,
            BusinessCapability.AccountManage,
            "ACCOUNT_ROLES_CHANGE",
            targetUserId.ToString(),
            correlationId,
            cancellationToken);
        var distinctRoles = roles.Distinct().Order().ToArray();
        if (distinctRoles.Length == 0 || !distinctRoles.Contains(primaryRole))
        {
            await AppendAuditAsync(
                new BusinessAuditWrite(
                    BusinessAuditActor.From(actor),
                    grant.GrantedByRole,
                    BusinessCapability.AccountManage,
                    "ACCOUNT_ROLES_CHANGE",
                    "UserAccount",
                    targetUserId.ToString(),
                    BusinessAuditResult.Denied,
                    "PRIMARY_ROLE_MUST_BE_ASSIGNED",
                    correlationId),
                cancellationToken);
            throw new ArgumentException(
                "The primary role must be included in the assigned roles.",
                nameof(primaryRole));
        }

        var target = await context.UserAccounts
            .Include(user => user.RoleAssignments)
            .SingleOrDefaultAsync(user => user.Id == targetUserId, cancellationToken)
            ?? throw new KeyNotFoundException("The target user account does not exist.");
        target.PrimaryRole = primaryRole;
        target.RoleAssignments.Clear();
        foreach (var role in distinctRoles)
        {
            target.RoleAssignments.Add(new UserRoleAssignment
            {
                UserAccountId = target.Id,
                Role = role,
                UserAccount = target,
            });
        }

        auditWriter.Append(new BusinessAuditWrite(
            BusinessAuditActor.From(actor),
            grant.GrantedByRole,
            BusinessCapability.AccountManage,
            "ACCOUNT_ROLES_CHANGE",
            "UserAccount",
            targetUserId.ToString(),
            BusinessAuditResult.Succeeded,
            null,
            correlationId));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BusinessAuditRecord>> ReadAuditAsync(
        EffectiveIdentity actor,
        int take,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var grant = await DemandAsync(
            actor,
            BusinessCapability.BusinessAuditRead,
            "BUSINESS_AUDIT_READ",
            actor.UserId.ToString(),
            correlationId,
            cancellationToken);
        await AppendAuditAsync(
            new BusinessAuditWrite(
                BusinessAuditActor.From(actor),
                grant.GrantedByRole,
                BusinessCapability.BusinessAuditRead,
                "BUSINESS_AUDIT_READ",
                "BusinessAudit",
                actor.UserId.ToString(),
                BusinessAuditResult.Succeeded,
                null,
                correlationId),
            cancellationToken);
        return await context.BusinessAuditRecords
            .AsNoTracking()
            .OrderByDescending(audit => audit.OccurredAtUtc)
            .Take(Math.Clamp(take, 1, 200))
            .ToArrayAsync(cancellationToken);
    }

    public async Task DemandCapabilityAsync(
        EffectiveIdentity actor,
        BusinessCapability capability,
        string action,
        string objectType,
        string objectId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await DemandAsync(
            actor,
            capability,
            action,
            objectId,
            correlationId,
            cancellationToken,
            objectType);
    }

    public async Task RecordInactiveRequestDeniedAsync(
        EffectiveIdentity actor,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await AppendAuditAsync(
            new BusinessAuditWrite(
                BusinessAuditActor.From(actor),
                null,
                null,
                "AUTHENTICATED_REQUEST_REJECTED",
                "UserAccount",
                actor.UserId.ToString(),
                BusinessAuditResult.Denied,
                "IDENTITY_NOT_ACTIVE",
                correlationId),
            cancellationToken);
    }

    public async Task SetAccountActiveAsync(
        EffectiveIdentity actor,
        Guid targetUserId,
        bool isActive,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var grant = await DemandAsync(
            actor,
            BusinessCapability.AccountManage,
            "ACCOUNT_STATUS_CHANGE",
            targetUserId.ToString(),
            correlationId,
            cancellationToken);
        var target = await context.UserAccounts.SingleOrDefaultAsync(
            user => user.Id == targetUserId,
            cancellationToken) ?? throw new KeyNotFoundException(
                "The target user account does not exist.");
        target.IsActive = isActive;
        auditWriter.Append(new BusinessAuditWrite(
            BusinessAuditActor.From(actor),
            grant.GrantedByRole,
            BusinessCapability.AccountManage,
            "ACCOUNT_STATUS_CHANGE",
            "UserAccount",
            targetUserId.ToString(),
            BusinessAuditResult.Succeeded,
            null,
            correlationId));
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<CapabilityGrant> DemandAsync(
        EffectiveIdentity actor,
        BusinessCapability capability,
        string action,
        string objectId,
        string correlationId,
        CancellationToken cancellationToken,
        string objectType = "UserAccount")
    {
        var grant = actor.IsActive
            ? RoleCapabilityMatrix.Authorize(actor.Roles, capability)
            : new CapabilityGrant(false, null);
        if (grant.IsGranted)
        {
            return grant;
        }

        await AppendAuditAsync(
            new BusinessAuditWrite(
                BusinessAuditActor.From(actor),
                null,
                capability,
                action,
                objectType,
                objectId,
                BusinessAuditResult.Denied,
                actor.IsActive ? "CAPABILITY_NOT_GRANTED" : "ACTOR_NOT_ACTIVE",
                correlationId),
            cancellationToken);
        throw new CapabilityDeniedException(
            capability,
            actor.IsActive ? "CAPABILITY_NOT_GRANTED" : "ACTOR_NOT_ACTIVE");
    }

    private async Task AppendAuditAsync(
        BusinessAuditWrite write,
        CancellationToken cancellationToken)
    {
        auditWriter.Append(write);
        await context.SaveChangesAsync(cancellationToken);
    }
}
