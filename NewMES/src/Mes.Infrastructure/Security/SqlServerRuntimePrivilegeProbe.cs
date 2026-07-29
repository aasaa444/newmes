using System.Data;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Security;

public sealed record DatabasePrivilegeResult(
    bool IsLeastPrivilege,
    IReadOnlyList<string> ViolationCodes);

public interface IRuntimeDatabasePrivilegeProbe
{
    Task<DatabasePrivilegeResult> CheckAsync(
        CancellationToken cancellationToken = default);
}

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
