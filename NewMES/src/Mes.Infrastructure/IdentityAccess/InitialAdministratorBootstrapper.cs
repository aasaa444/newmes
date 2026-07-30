using System.Data;
using Mes.Domain.Auditing;
using Mes.Domain.Identity;
using Mes.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.IdentityAccess;

/// <summary>
/// 仅由独立数据库迁移工具显式创建首位管理员，避免 API 启动时悄悄产生默认高权限账号。
/// </summary>
public sealed class InitialAdministratorBootstrapper(
    MesDbContext context,
    IPasswordHasher<UserAccount> passwordHasher,
    TimeProvider timeProvider)
{
    private readonly BusinessAuditWriter auditWriter = new(context, timeProvider);

    public async Task BootstrapAsync(
        string username,
        string displayName,
        string password,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var normalizedUsername = RequireValue(username, 80, nameof(username));
        var normalizedDisplayName = RequireValue(displayName, 120, nameof(displayName));
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
        {
            throw new ArgumentException(
                "The initial administrator password must contain at least 12 characters.",
                nameof(password));
        }

        if ((await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
        {
            throw new InvalidOperationException(
                "The initial administrator cannot be provisioned before all migrations are applied.");
        }

        var strategy = context.Database.CreateExecutionStrategy();
        var failureReason = await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            string? reason = null;
            if (await context.UserRoleAssignments.AnyAsync(
                    assignment => assignment.Role == BusinessRole.SystemAdministrator,
                    cancellationToken))
            {
                reason = "INITIAL_ADMIN_ALREADY_EXISTS";
            }
            else if (await context.UserAccounts.AnyAsync(
                         account => account.Username == normalizedUsername,
                         cancellationToken))
            {
                reason = "USERNAME_ALREADY_EXISTS";
            }

            if (reason is not null)
            {
                AddAudit(
                    normalizedUsername,
                    correlationId,
                    BusinessAuditResult.Denied,
                    reason);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return reason;
            }

            var account = new UserAccount
            {
                Id = Guid.NewGuid(),
                Username = normalizedUsername,
                DisplayName = normalizedDisplayName,
                IsActive = true,
                PrimaryRole = BusinessRole.SystemAdministrator,
            };
            account.PasswordHash = passwordHasher.HashPassword(account, password);
            account.RoleAssignments.Add(new UserRoleAssignment
            {
                UserAccountId = account.Id,
                Role = BusinessRole.SystemAdministrator,
                UserAccount = account,
            });
            context.UserAccounts.Add(account);
            AddAudit(
                normalizedUsername,
                correlationId,
                BusinessAuditResult.Succeeded,
                null);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        });

        if (failureReason is not null)
        {
            throw new InvalidOperationException(
                $"Initial administrator bootstrap was rejected: {failureReason}.");
        }
    }

    private void AddAudit(
        string targetUsername,
        string correlationId,
        BusinessAuditResult result,
        string? reasonCode)
    {
        auditWriter.Append(new BusinessAuditWrite(
            new BusinessAuditActor(null, "deployment.bootstrap", []),
            null,
            null,
            "INITIAL_ADMIN_BOOTSTRAP",
            "UserAccount",
            targetUsername,
            result,
            reasonCode,
            correlationId));
    }

    private static string RequireValue(string value, int maxLength, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
        {
            throw new ArgumentException(
                $"The value must contain between 1 and {maxLength} characters.",
                parameterName);
        }

        return normalized;
    }
}
