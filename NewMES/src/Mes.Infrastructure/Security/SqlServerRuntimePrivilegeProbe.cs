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
            SELECT
                COALESCE(IS_SRVROLEMEMBER('sysadmin'), 0),
                COALESCE(IS_MEMBER('db_owner'), 0),
                COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL'), 0),
                COALESCE(HAS_PERMS_BY_NAME(NULL, NULL, 'CONTROL SERVER'), 0);
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

            var isElevated = Enumerable.Range(0, 4)
                .Any(index => reader.GetInt32(index) == 1);
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
