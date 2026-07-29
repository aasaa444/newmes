using Mes.Infrastructure.Persistence;

namespace Mes.Api.Readiness;

public sealed partial class DatabaseCompatibilityStartupReporter(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseCompatibilityStartupReporter> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var checker = scope.ServiceProvider.GetRequiredService<DatabaseCompatibilityChecker>();
        var result = await checker.CheckAsync(cancellationToken);
        if (result.IsReady)
        {
            LogDatabaseReady(logger, result.AppliedMigrations.Count);
            return;
        }

        LogDatabaseBlocked(logger, result.Code, result.Message);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Database compatibility check passed with {MigrationCount} migrations.")]
    private static partial void LogDatabaseReady(ILogger logger, int migrationCount);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Critical,
        Message = "Database readiness blocked. Code={Code}; Message={Message}")]
    private static partial void LogDatabaseBlocked(
        ILogger logger,
        string code,
        string message);
}
