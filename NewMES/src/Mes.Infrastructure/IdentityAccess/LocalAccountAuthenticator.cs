using Mes.Domain.Auditing;
using Mes.Domain.Identity;
using Mes.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.IdentityAccess;

public sealed class LocalAccountAuthenticator(
    MesDbContext context,
    IPasswordHasher<UserAccount> passwordHasher,
    TimeProvider timeProvider)
{
    public async Task<EffectiveIdentity?> AuthenticateAsync(
        string username,
        string password,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var normalizedUsername = username.Trim();
        var account = await context.UserAccounts
            .Include(user => user.RoleAssignments)
            .SingleOrDefaultAsync(
                user => user.Username == normalizedUsername,
                cancellationToken);
        var verified = account is not null
            && account.IsActive
            && account.PasswordHash is not null
            && passwordHasher.VerifyHashedPassword(
                account,
                account.PasswordHash,
                password) != PasswordVerificationResult.Failed;
        context.BusinessAuditRecords.Add(new BusinessAuditRecord
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = timeProvider.GetUtcNow(),
            ActorUserId = account?.Id,
            ActorUsername = normalizedUsername,
            Action = "LOCAL_LOGIN",
            BusinessObjectType = "UserAccount",
            BusinessObjectId = account?.Id.ToString() ?? normalizedUsername,
            Result = verified ? BusinessAuditResult.Succeeded : BusinessAuditResult.Denied,
            ReasonCode = verified ? null : "INVALID_CREDENTIALS_OR_INACTIVE_ACCOUNT",
            CorrelationId = correlationId,
        });
        await context.SaveChangesAsync(cancellationToken);
        if (!verified || account is null)
        {
            return null;
        }

        var roles = account.RoleAssignments.Select(assignment => assignment.Role)
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
}
