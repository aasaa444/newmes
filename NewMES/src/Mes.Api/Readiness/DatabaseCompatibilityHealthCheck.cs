using Mes.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Mes.Api.Readiness;

public sealed class DatabaseCompatibilityHealthCheck(
    DatabaseCompatibilityChecker checker) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var compatibility = await checker.CheckAsync(cancellationToken);
        var data = new Dictionary<string, object>
        {
            ["code"] = compatibility.Code,
            ["appliedMigrations"] = compatibility.AppliedMigrations,
            ["pendingMigrations"] = compatibility.PendingMigrations,
            ["unknownMigrations"] = compatibility.UnknownMigrations,
        };

        return compatibility.IsReady
            ? HealthCheckResult.Healthy(compatibility.Message, data)
            : HealthCheckResult.Unhealthy(compatibility.Message, data: data);
    }
}
