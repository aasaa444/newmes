using System.Data;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Security;

// 探针结果只公开稳定违规代码，避免在 readiness 响应中泄露数据库登录或权限细节。
public sealed record DatabasePrivilegeResult(
    bool IsLeastPrivilege,
    IReadOnlyList<string> ViolationCodes);

public interface IRuntimeDatabasePrivilegeProbe
{
    Task<DatabasePrivilegeResult> CheckAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 在数据库会话中核验 API 账号不是服务器管理员、数据库所有者或 DDL 管理员。
/// 架构迁移由独立迁移身份执行，运行期 API 只应拥有业务读写权限。
/// </summary>
public sealed class SqlServerRuntimePrivilegeProbe(
    MesDbContext context) : IRuntimeDatabasePrivilegeProbe
{
    public async Task<DatabasePrivilegeResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CAST(CASE WHEN
                COALESCE(IS_SRVROLEMEMBER('sysadmin'), 0) = 1 OR
                COALESCE(IS_SRVROLEMEMBER('serveradmin'), 0) = 1 OR
                COALESCE(IS_SRVROLEMEMBER('securityadmin'), 0) = 1 OR
                COALESCE(IS_SRVROLEMEMBER('dbcreator'), 0) = 1 OR
                COALESCE(IS_MEMBER('db_owner'), 0) = 1 OR
                COALESCE(IS_MEMBER('db_accessadmin'), 0) = 1 OR
                COALESCE(IS_MEMBER('db_securityadmin'), 0) = 1 OR
                COALESCE(IS_MEMBER('db_ddladmin'), 0) = 1 OR
                COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL'), 0) = 1 OR
                COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER'), 0) = 1 OR
                COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE'), 0) = 1 OR
                COALESCE(HAS_PERMS_BY_NAME('mes', 'SCHEMA', 'CONTROL'), 0) = 1 OR
                COALESCE(HAS_PERMS_BY_NAME('mes', 'SCHEMA', 'ALTER'), 0) = 1 OR
                COALESCE(HAS_PERMS_BY_NAME(NULL, NULL, 'CONTROL SERVER'), 0) = 1
                THEN 1 ELSE 0 END AS bit);
            """;
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        try
        {
            if (shouldClose)
            {
                await context.Database.OpenConnectionAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "SQL Server did not return runtime privilege information.");
            }

            var isElevated = reader.GetBoolean(0);
            return isElevated
                ? new DatabasePrivilegeResult(false, ["SEC_DATABASE_HIGH_PRIVILEGE"])
                : new DatabasePrivilegeResult(true, []);
        }
        finally
        {
            if (shouldClose)
            {
                await context.Database.CloseConnectionAsync();
            }
        }
    }
}
