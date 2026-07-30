using Mes.Api.Identity;
using Mes.Infrastructure.Execution;
using Mes.Infrastructure.IdentityAccess;

namespace Mes.Api.Execution;

public static class TestRunEndpoints
{
    public static IEndpointRouteBuilder MapTestRunEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/execution/tests/{finishedSerialNumber}/runs", ExecuteAsync)
            .RequireAuthorization();
        endpoints.MapGet("/api/execution/tests/{finishedSerialNumber}", ReadWorkstationAsync)
            .RequireAuthorization();
        endpoints.MapGet("/api/genealogy/products/{finishedSerialNumber}/tests", ReadGenealogyAsync)
            .RequireAuthorization();
        return endpoints;
    }

    private static Task<IResult> ExecuteAsync(
        string finishedSerialNumber,
        TestRunRequest request,
        CurrentIdentityAccessor accessor,
        TestRunService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteEndpointAsync(
            accessor,
            context.TraceIdentifier,
            async () =>
            {
                var result = await service.ExecuteAsync(
                    accessor.Identity!,
                    finishedSerialNumber,
                    request,
                    context.TraceIdentifier,
                    cancellationToken);
                return result.IsReplay
                    ? Results.Ok(result)
                    : Results.Created($"/api/genealogy/products/{finishedSerialNumber}/tests", result);
            });

    private static Task<IResult> ReadWorkstationAsync(
        string finishedSerialNumber,
        CurrentIdentityAccessor accessor,
        TestRunService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteEndpointAsync(
            accessor,
            context.TraceIdentifier,
            async () => Results.Ok(await service.ReadWorkstationAsync(
                accessor.Identity!,
                finishedSerialNumber,
                context.TraceIdentifier,
                cancellationToken)));

    private static Task<IResult> ReadGenealogyAsync(
        string finishedSerialNumber,
        CurrentIdentityAccessor accessor,
        TestRunService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteEndpointAsync(
            accessor,
            context.TraceIdentifier,
            async () => Results.Ok(await service.ReadGenealogyAsync(
                accessor.Identity!,
                finishedSerialNumber,
                context.TraceIdentifier,
                cancellationToken)));

    private static async Task<IResult> ExecuteEndpointAsync(
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
                    message = "请先登录后再访问测试执行。",
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
                    message = "当前账号没有执行该测试操作的权限，请核对岗位角色。",
                    correlationId,
                },
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (TestRunRejectedException exception)
        {
            return Results.Json(
                new { code = exception.Code, message = exception.Message, correlationId },
                statusCode: exception.HttpStatusCode);
        }
    }
}
