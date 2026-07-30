using Mes.Api.Identity;
using Mes.Infrastructure.Execution;
using Mes.Infrastructure.IdentityAccess;

namespace Mes.Api.Execution;

/// <summary>装配投料、纠错及物料谱系端点；业务校验和事务仍由应用服务统一负责。</summary>
public static class AssemblyMaterialEndpoints
{
    public static IEndpointRouteBuilder MapAssemblyMaterialEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/execution/assembly/{finishedSerialNumber}/materials/consume",
                ConsumeAsync)
            .RequireAuthorization();
        endpoints.MapGet(
                "/api/execution/assembly/{finishedSerialNumber}/materials",
                ReadRequirementsAsync)
            .RequireAuthorization();
        endpoints.MapGet(
                "/api/genealogy/products/{finishedSerialNumber}/materials",
                ReadProductGenealogyAsync)
            .RequireAuthorization();
        endpoints.MapGet(
                "/api/genealogy/lots/{lotNumber}/products",
                ReadLotImpactAsync)
            .RequireAuthorization();
        endpoints.MapGet(
                "/api/genealogy/components/{componentSerialNumber}/products",
                ReadComponentImpactAsync)
            .RequireAuthorization();
        endpoints.MapPost(
                "/api/execution/assembly/bindings/{bindingId:guid}/unbind",
                UnbindAsync)
            .RequireAuthorization();
        endpoints.MapPost(
                "/api/execution/assembly/bindings/{bindingId:guid}/replace",
                ReplaceAsync)
            .RequireAuthorization();
        endpoints.MapPost(
                "/api/execution/assembly/consumptions/{transactionId:guid}/reverse",
                ReverseConsumptionAsync)
            .RequireAuthorization();
        return endpoints;
    }

    private static Task<IResult> ConsumeAsync(
        string finishedSerialNumber,
        AssemblyMaterialConsumeRequest request,
        CurrentIdentityAccessor accessor,
        AssemblyMaterialService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            async () =>
            {
                var result = await service.ConsumeAsync(
                    accessor.Identity!,
                    finishedSerialNumber,
                    request,
                    context.TraceIdentifier,
                    cancellationToken);
                return result.IsReplay
                    ? Results.Ok(result)
                    : Results.Created(
                        $"/api/genealogy/products/{finishedSerialNumber}/materials",
                        result);
            });

    private static Task<IResult> ReadRequirementsAsync(
        string finishedSerialNumber,
        CurrentIdentityAccessor accessor,
        AssemblyMaterialService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            async () => Results.Ok(await service.ReadRequirementsAsync(
                accessor.Identity!,
                finishedSerialNumber,
                context.TraceIdentifier,
                cancellationToken)));

    private static Task<IResult> ReadProductGenealogyAsync(
        string finishedSerialNumber,
        CurrentIdentityAccessor accessor,
        AssemblyMaterialService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            async () => Results.Ok(await service.ReadProductGenealogyAsync(
                accessor.Identity!,
                finishedSerialNumber,
                context.TraceIdentifier,
                cancellationToken)));

    private static Task<IResult> ReadLotImpactAsync(
        string lotNumber,
        string materialCode,
        CurrentIdentityAccessor accessor,
        AssemblyMaterialService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            async () => Results.Ok(await service.ReadLotImpactAsync(
                accessor.Identity!,
                materialCode,
                lotNumber,
                context.TraceIdentifier,
                cancellationToken)));

    private static Task<IResult> ReadComponentImpactAsync(
        string componentSerialNumber,
        CurrentIdentityAccessor accessor,
        AssemblyMaterialService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            async () => Results.Ok(await service.ReadComponentImpactAsync(
                accessor.Identity!,
                componentSerialNumber,
                context.TraceIdentifier,
                cancellationToken)));

    private static Task<IResult> UnbindAsync(
        Guid bindingId,
        AssemblyBindingUnbindRequest request,
        CurrentIdentityAccessor accessor,
        AssemblyMaterialService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            async () => Results.Ok(await service.UnbindAsync(
                accessor.Identity!,
                bindingId,
                request,
                context.TraceIdentifier,
                cancellationToken)));

    private static Task<IResult> ReplaceAsync(
        Guid bindingId,
        AssemblyBindingReplaceRequest request,
        CurrentIdentityAccessor accessor,
        AssemblyMaterialService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            async () => Results.Ok(await service.ReplaceAsync(
                accessor.Identity!,
                bindingId,
                request,
                context.TraceIdentifier,
                cancellationToken)));

    private static Task<IResult> ReverseConsumptionAsync(
        Guid transactionId,
        AssemblyConsumptionReverseRequest request,
        CurrentIdentityAccessor accessor,
        AssemblyMaterialService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            async () => Results.Ok(await service.ReverseConsumptionAsync(
                accessor.Identity!,
                transactionId,
                request,
                context.TraceIdentifier,
                cancellationToken)));

    private static async Task<IResult> ExecuteAsync(
        CurrentIdentityAccessor accessor,
        Func<Task<IResult>> execute)
    {
        if (accessor.Identity is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return await execute();
        }
        catch (CapabilityDeniedException exception)
        {
            return Results.Json(
                new { code = exception.ReasonCode, capability = exception.Capability.ToString() },
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (AssemblyMaterialRejectedException exception)
        {
            return Results.Json(
                new { code = exception.Code, message = exception.Message },
                statusCode: exception.HttpStatusCode);
        }
    }
}
