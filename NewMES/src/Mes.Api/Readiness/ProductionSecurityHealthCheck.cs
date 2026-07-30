using Mes.Infrastructure.Security;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Mes.Api.Readiness;

/// <summary>在生产模式下核验配置安全基线和数据库最小权限，任一违规都会关闭 readiness。</summary>
public sealed partial class ProductionSecurityHealthCheck(
    ProductionSecurityContextProvider contextProvider,
    IRuntimeDatabasePrivilegeProbe privilegeProbe,
    ILogger<ProductionSecurityHealthCheck> logger) : IHealthCheck
{
    private static readonly string[] NonProductionCodes = ["SEC_NON_PRODUCTION"];
    private static readonly string[] PrivilegeCheckFailedCodes =
        ["SEC_DATABASE_PRIVILEGE_CHECK_FAILED"];

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!contextProvider.RequiresProductionBaseline)
        {
            return HealthCheckResult.Healthy(
                "Production security baseline is not enforced in this non-production deployment.",
                new Dictionary<string, object>
                {
                    ["codes"] = NonProductionCodes,
                });
        }

        var baseline = ProductionSecurityBaseline.Evaluate(contextProvider.Read());
        if (!baseline.IsReady)
        {
            return HealthCheckResult.Unhealthy(
                "Production security baseline failed.",
                data: new Dictionary<string, object>
                {
                    ["codes"] = baseline.Violations
                        .Select(violation => violation.Code)
                        .ToArray(),
                });
        }

        try
        {
            var privileges = await privilegeProbe.CheckAsync(cancellationToken);
            return privileges.IsLeastPrivilege
                ? HealthCheckResult.Healthy(
                    "Production security baseline passed.",
                    new Dictionary<string, object>
                    {
                        ["codes"] = Array.Empty<string>(),
                    })
                : HealthCheckResult.Unhealthy(
                    "The API database account has elevated privileges.",
                    data: new Dictionary<string, object>
                    {
                        ["codes"] = privileges.ViolationCodes,
                    });
        }
        catch (Exception exception)
        {
            LogPrivilegeCheckFailed(logger, exception);
            return HealthCheckResult.Unhealthy(
                "Database privilege verification failed. See the protected application log.",
                data: new Dictionary<string, object>
                {
                    ["codes"] = PrivilegeCheckFailedCodes,
                });
        }
    }

    [LoggerMessage(
        EventId = 1103,
        Level = LogLevel.Error,
        Message = "Database runtime privilege verification failed.")]
    private static partial void LogPrivilegeCheckFailed(
        ILogger logger,
        Exception exception);
}
