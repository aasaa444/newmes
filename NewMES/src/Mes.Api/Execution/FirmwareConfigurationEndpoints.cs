using Mes.Api.Identity;
using Mes.Infrastructure.Execution;
using Mes.Infrastructure.IdentityAccess;

namespace Mes.Api.Execution;

public static class FirmwareConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapFirmwareConfigurationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/execution/firmware/{finishedSerialNumber}/executions",
                ExecuteAsync)
            .RequireAuthorization();
        endpoints.MapGet(
                "/api/execution/firmware/{finishedSerialNumber}",
                ReadWorkstationAsync)
            .RequireAuthorization();
        endpoints.MapGet(
                "/api/genealogy/products/{finishedSerialNumber}/firmware",
                ReadGenealogyAsync)
            .RequireAuthorization();
        return endpoints;
    }

    private static Task<IResult> ExecuteAsync(
        string finishedSerialNumber,
        FirmwareExecutionRequest request,
        CurrentIdentityAccessor accessor,
        FirmwareConfigurationService service,
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
                    : Results.Created(
                        $"/api/genealogy/products/{finishedSerialNumber}/firmware",
                        result);
            });

    private static Task<IResult> ReadWorkstationAsync(
        string finishedSerialNumber,
        CurrentIdentityAccessor accessor,
        FirmwareConfigurationService service,
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
        FirmwareConfigurationService service,
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
                    message = "请先登录后再执行固件配置操作。",
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
                    message = "当前账号没有执行该固件配置操作的权限，请联系管理员核对岗位角色。",
                    correlationId,
                },
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (FirmwareConfigurationRejectedException exception)
        {
            return Results.Json(
                new { code = exception.Code, message = exception.Message, correlationId },
                statusCode: exception.HttpStatusCode);
        }
    }
}
