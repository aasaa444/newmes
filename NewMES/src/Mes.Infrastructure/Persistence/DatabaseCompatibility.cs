using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Persistence;

public sealed record DatabaseCompatibility(
    bool IsReady,
    string Code,
    string Message,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> PendingMigrations,
    IReadOnlyList<string> UnknownMigrations,
    Exception? Error = null);

public sealed class DatabaseCompatibilityChecker(MesDbContext context)
{
    public async Task<DatabaseCompatibility> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await context.Database.CanConnectAsync(cancellationToken))
            {
                return NotReady(
                    "DB_UNAVAILABLE",
                    "SQL Server database is missing or unavailable.");
            }

            var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken))
                .ToArray();
            var known = context.Database.GetMigrations()
                .ToHashSet(StringComparer.Ordinal);
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken))
                .ToArray();
            var unknown = applied.Where(migration => !known.Contains(migration)).ToArray();

            if (unknown.Length > 0)
            {
                return new DatabaseCompatibility(
                    false,
                    "DB_SCHEMA_NEWER_THAN_APPLICATION",
                    $"Database contains unknown migrations: {string.Join(", ", unknown)}.",
                    applied,
                    pending,
                    unknown);
            }

            if (pending.Length > 0)
            {
                return new DatabaseCompatibility(
                    false,
                    "DB_SCHEMA_OUTDATED",
                    $"Database requires migrations: {string.Join(", ", pending)}.",
                    applied,
                    pending,
                    []);
            }

            return new DatabaseCompatibility(
                true,
                "DB_READY",
                "Database schema is compatible with this application version.",
                applied,
                [],
                []);
        }
        catch (Exception exception)
        {
            return new DatabaseCompatibility(
                false,
                "DB_COMPATIBILITY_CHECK_FAILED",
                "Database compatibility check failed. See the protected application log for details.",
                [],
                [],
                [],
                exception);
        }
    }

    private static DatabaseCompatibility NotReady(string code, string message) =>
        new(false, code, message, [], [], []);
}
