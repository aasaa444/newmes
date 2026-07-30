using Mes.Domain.Auditing;
using Mes.Domain.Identity;
using Mes.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.IdentityAccess;

/// <summary>验证本地账号密码并返回请求期身份；停用账号即使密码正确也不能建立会话。</summary>
public sealed class LocalAccountAuthenticator(
    MesDbContext context,
    IPasswordHasher<UserAccount> passwordHasher,
    TimeProvider timeProvider)
{
    private readonly BusinessAuditWriter auditWriter = new(context, timeProvider);

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
        auditWriter.Append(new BusinessAuditWrite(
            new BusinessAuditActor(
                account?.Id,
                normalizedUsername,
                account?.RoleAssignments.Select(assignment => assignment.Role).ToArray() ?? []),
            null,
            null,
            "LOCAL_LOGIN",
            "UserAccount",
            account?.Id.ToString() ?? normalizedUsername,
            verified ? BusinessAuditResult.Succeeded : BusinessAuditResult.Denied,
            verified ? null : "INVALID_CREDENTIALS_OR_INACTIVE_ACCOUNT",
            correlationId));
        await context.SaveChangesAsync(cancellationToken);
        if (!verified || account is null)
        {
            return null;
        }

        return EffectiveIdentityProjector.From(account);
    }
}
