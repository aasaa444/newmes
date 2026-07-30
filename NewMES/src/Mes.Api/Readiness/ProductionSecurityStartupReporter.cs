using Mes.Infrastructure.Security;

namespace Mes.Api.Readiness;

/// <summary>启动时把生产安全违规写入受保护日志，供部署人员定位而不向匿名健康响应泄密。</summary>
public sealed partial class ProductionSecurityStartupReporter(
    ProductionSecurityContextProvider contextProvider,
    ILogger<ProductionSecurityStartupReporter> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!contextProvider.RequiresProductionBaseline)
        {
            LogNotEnforced(logger);
            return Task.CompletedTask;
        }

        var result = ProductionSecurityBaseline.Evaluate(contextProvider.Read());
        if (result.IsReady)
        {
            LogBaselinePassed(logger);
        }
        else
        {
            var violationCodes = string.Join(",", result.Violations
                .Select(violation => violation.Code));
            LogBaselineBlocked(logger, violationCodes);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Information,
        Message = "Production security baseline is not enforced for this non-production deployment.")]
    private static partial void LogNotEnforced(ILogger logger);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Information,
        Message = "Production security configuration baseline passed.")]
    private static partial void LogBaselinePassed(ILogger logger);

    [LoggerMessage(
        EventId = 1102,
        Level = LogLevel.Critical,
        Message = "Production security readiness blocked. Codes={Codes}")]
    private static partial void LogBaselineBlocked(ILogger logger, string codes);
}
