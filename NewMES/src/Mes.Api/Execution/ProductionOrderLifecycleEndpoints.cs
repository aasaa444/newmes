using Mes.Api.Identity;
using Mes.Infrastructure.Execution;
using Mes.Infrastructure.IdentityAccess;

namespace Mes.Api.Execution;

/// <summary>生产订单受控状态命令和释放快照读取端点；不提供任意状态更新接口。</summary>
public static class ProductionOrderLifecycleEndpoints
{
    public static IEndpointRouteBuilder MapProductionOrderLifecycleEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        MapCommand(endpoints, "release", ProductionOrderCommand.Release);
        MapCommand(endpoints, "pause", ProductionOrderCommand.Pause);
        MapCommand(endpoints, "resume", ProductionOrderCommand.Resume);
        MapCommand(endpoints, "cancel", ProductionOrderCommand.Cancel);
        MapCommand(endpoints, "complete-execution", ProductionOrderCommand.CompleteExecution);
        MapCommand(endpoints, "close", ProductionOrderCommand.Close);
        endpoints.MapGet(
                "/api/planning/production-orders/{orderId:guid}/execution-snapshot",
                ReadSnapshotAsync)
            .RequireAuthorization();
        return endpoints;
    }

    private static void MapCommand(
        IEndpointRouteBuilder endpoints,
        string routeCommand,
        ProductionOrderCommand command) => endpoints.MapPost(
            $"/api/planning/production-orders/{{orderId:guid}}/{routeCommand}",
            async (
                Guid orderId,
                CurrentIdentityAccessor accessor,
                ProductionOrderLifecycleService service,
                HttpContext context,
                CancellationToken cancellationToken) => await ExecuteAsync(
                    orderId,
                    command,
                    accessor,
                    service,
                    context,
                    cancellationToken))
            .RequireAuthorization();

    private static async Task<IResult> ExecuteAsync(
        Guid orderId,
        ProductionOrderCommand command,
        CurrentIdentityAccessor accessor,
        ProductionOrderLifecycleService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (accessor.Identity is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await service.ExecuteAsync(
                accessor.Identity,
                orderId,
                command,
                context.TraceIdentifier,
                cancellationToken));
        }
        catch (CapabilityDeniedException exception)
        {
            return Forbidden(exception);
        }
        catch (ProductionOrderCommandRejectedException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> ReadSnapshotAsync(
        Guid orderId,
        CurrentIdentityAccessor accessor,
        ProductionOrderLifecycleService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (accessor.Identity is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Content(
                await service.ReadSnapshotJsonAsync(
                    accessor.Identity,
                    orderId,
                    context.TraceIdentifier,
                    cancellationToken),
                "application/json");
        }
        catch (CapabilityDeniedException exception)
        {
            return Forbidden(exception);
        }
        catch (ProductionOrderCommandRejectedException exception)
        {
            return Rejected(exception);
        }
    }

    private static IResult Forbidden(CapabilityDeniedException exception) =>
        Results.Json(
            new { code = exception.ReasonCode, capability = exception.Capability.ToString() },
            statusCode: StatusCodes.Status403Forbidden);

    private static IResult Rejected(ProductionOrderCommandRejectedException exception) =>
        Results.Json(
            new { code = exception.Code, message = exception.Message },
            statusCode: exception.HttpStatusCode);
}
