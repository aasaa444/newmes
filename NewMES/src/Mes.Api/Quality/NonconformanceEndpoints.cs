using Mes.Api.Identity;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Quality;

namespace Mes.Api.Quality;

/// <summary>质量工作台的不合格与保留查询端点；Ticket 11 不暴露处置或解除保留命令。</summary>
public static class NonconformanceEndpoints
{
    public static IEndpointRouteBuilder MapNonconformanceEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/quality/nonconformances", ReportAsync)
            .RequireAuthorization();
        endpoints.MapGet("/api/quality/nonconformances", ReadAsync)
            .RequireAuthorization();
        return endpoints;
    }

    private static Task<IResult> ReportAsync(
        NonconformanceReportRequest request,
        CurrentIdentityAccessor accessor,
        NonconformanceService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            context.TraceIdentifier,
            async () =>
            {
                var result = await service.ReportAsync(
                    accessor.Identity!,
                    request,
                    context.TraceIdentifier,
                    cancellationToken);
                return result.IsReplay
                    ? Results.Ok(result)
                    : Results.Created($"/api/quality/nonconformances/{result.NonconformanceId}", result);
            });

    private static async Task<IResult> ReadAsync(
        string? finishedSerialNumber,
        CurrentIdentityAccessor accessor,
        NonconformanceQueryService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(
            accessor,
            context.TraceIdentifier,
            async () => Results.Ok(await service.ReadAsync(
                accessor.Identity!,
                finishedSerialNumber,
                context.TraceIdentifier,
                cancellationToken)));
    }

    private static async Task<IResult> ExecuteAsync(
        CurrentIdentityAccessor accessor,
        string correlationId,
        Func<Task<IResult>> execute)
    {
        if (accessor.Identity is null)
        {
            return Results.Json(
                new
                {
                    code = "AUTHENTICATION_REQUIRED",
                    message = "请先登录后再访问质量业务。",
                    correlationId,
                },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        try
        {
            return await execute();
        }
        catch (CapabilityDeniedException exception)
        {
            return Results.Json(
                new
                {
                    code = exception.ReasonCode,
                    capability = exception.Capability.ToString(),
                    message = "当前账号没有执行该质量操作的权限。",
                    correlationId,
                },
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (NonconformanceRejectedException exception)
        {
            return Results.Json(
                new { code = exception.Code, message = exception.Message, correlationId },
                statusCode: exception.HttpStatusCode);
        }
    }
}
