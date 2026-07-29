using Mes.Domain.Auditing;
using Mes.Domain.Identity;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.IdentityAccess;

public sealed class IdentityAccessService(
    MesDbContext context,
    TimeProvider timeProvider)
{
    public async Task<EffectiveIdentity> GetRequiredIdentityAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var account = await context.UserAccounts
            .AsNoTracking()
            .Include(user => user.RoleAssignments)
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("The user account does not exist.");
        var roles = account.RoleAssignments
            .Select(assignment => assignment.Role)
            .Distinct()
            .Order()
            .ToArray();
        return new EffectiveIdentity(
            account.Id,
            account.Username,
            account.DisplayName,
            account.IsActive,
            account.PrimaryRole,
            roles,
            RoleCapabilityMatrix.GetEffectiveCapabilities(roles));
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
            await AddAuditAsync(
                actor,
                grant.GrantedByRole,
                BusinessCapability.AccountManage,
                "ACCOUNT_ROLES_CHANGE",
                "UserAccount",
                targetUserId.ToString(),
                BusinessAuditResult.Denied,
                "PRIMARY_ROLE_MUST_BE_ASSIGNED",
                correlationId,
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

        AddAudit(
            actor,
            grant.GrantedByRole,
            BusinessCapability.AccountManage,
            "ACCOUNT_ROLES_CHANGE",
            "UserAccount",
            targetUserId.ToString(),
            BusinessAuditResult.Succeeded,
            null,
            correlationId);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BusinessAuditRecord>> ReadAuditAsync(
        EffectiveIdentity actor,
        int take,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await DemandAsync(
            actor,
            BusinessCapability.BusinessAuditRead,
            "BUSINESS_AUDIT_READ",
            actor.UserId.ToString(),
            correlationId,
            cancellationToken);
        return await context.BusinessAuditRecords
            .AsNoTracking()
            .OrderByDescending(audit => audit.OccurredAtUtc)
            .Take(Math.Clamp(take, 1, 200))
            .ToArrayAsync(cancellationToken);
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
        AddAudit(
            actor,
            grant.GrantedByRole,
            BusinessCapability.AccountManage,
            "ACCOUNT_STATUS_CHANGE",
            "UserAccount",
            targetUserId.ToString(),
            BusinessAuditResult.Succeeded,
            null,
            correlationId);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<CapabilityGrant> DemandAsync(
        EffectiveIdentity actor,
        BusinessCapability capability,
        string action,
        string objectId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var grant = actor.IsActive
            ? RoleCapabilityMatrix.Authorize(actor.Roles, capability)
            : new CapabilityGrant(false, null);
        if (grant.IsGranted)
        {
            return grant;
        }

        await AddAuditAsync(
            actor,
            null,
            capability,
            action,
            "UserAccount",
            objectId,
            BusinessAuditResult.Denied,
            actor.IsActive ? "CAPABILITY_NOT_GRANTED" : "ACTOR_NOT_ACTIVE",
            correlationId,
            cancellationToken);
        throw new CapabilityDeniedException(
            capability,
            actor.IsActive ? "CAPABILITY_NOT_GRANTED" : "ACTOR_NOT_ACTIVE");
    }

    private async Task AddAuditAsync(
        EffectiveIdentity actor,
        BusinessRole? authorizedRole,
        BusinessCapability capability,
        string action,
        string objectType,
        string objectId,
        BusinessAuditResult result,
        string? reasonCode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        AddAudit(
            actor,
            authorizedRole,
            capability,
            action,
            objectType,
            objectId,
            result,
            reasonCode,
            correlationId);
        await context.SaveChangesAsync(cancellationToken);
    }

    private void AddAudit(
        EffectiveIdentity actor,
        BusinessRole? authorizedRole,
        BusinessCapability capability,
        string action,
        string objectType,
        string objectId,
        BusinessAuditResult result,
        string? reasonCode,
        string correlationId)
    {
        context.BusinessAuditRecords.Add(new BusinessAuditRecord
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = timeProvider.GetUtcNow(),
            ActorUserId = actor.UserId,
            ActorUsername = actor.Username,
            AuthorizedRole = authorizedRole,
            Capability = capability,
            Action = action,
            BusinessObjectType = objectType,
            BusinessObjectId = objectId,
            Result = result,
            ReasonCode = reasonCode,
            CorrelationId = correlationId,
        });
    }
}
