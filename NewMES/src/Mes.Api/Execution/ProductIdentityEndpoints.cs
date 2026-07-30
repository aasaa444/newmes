using Mes.Api.Identity;
using Mes.Infrastructure.Execution;
using Mes.Infrastructure.IdentityAccess;

namespace Mes.Api.Execution;

/// <summary>受控身份源、序列号分配、开工和标签生命周期端点。</summary>
public static class ProductIdentityEndpoints
{
    public static IEndpointRouteBuilder MapProductIdentityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/configuration/identity-sources", RegisterSourceAsync)
            .RequireAuthorization();
        endpoints.MapPost("/api/integration/product-identities", AllocateAsync)
            .RequireAuthorization();
        endpoints.MapPost(
                "/api/execution/production-orders/{orderId:guid}/start-wip",
                StartWipAsync)
            .RequireAuthorization();
        endpoints.MapGet(
                "/api/execution/product-identities/by-serial/{serialNumber}/workstation",
                ReadWorkstationAsync)
            .RequireAuthorization();
        endpoints.MapPost(
                "/api/execution/product-identities/{identityId:guid}/unbind",
                UnbindAsync)
            .RequireAuthorization();
        endpoints.MapPost(
                "/api/execution/product-identities/{identityId:guid}/void",
                VoidIdentityAsync)
            .RequireAuthorization();
        endpoints.MapPost(
                "/api/execution/product-identities/{identityId:guid}/labels",
                PrintLabelAsync)
            .RequireAuthorization();
        endpoints.MapPost(
                "/api/execution/product-identities/{identityId:guid}/labels/{labelId:guid}/reprint",
                ReprintLabelAsync)
            .RequireAuthorization();
        endpoints.MapPost(
                "/api/execution/product-identities/{identityId:guid}/labels/{labelId:guid}/void",
                VoidLabelAsync)
            .RequireAuthorization();
        endpoints.MapPost(
                "/api/execution/product-identities/{identityId:guid}/labels/{labelId:guid}/replace",
                ReplaceLabelAsync)
            .RequireAuthorization();
        return endpoints;
    }

    private static Task<IResult> RegisterSourceAsync(
        IdentitySourceRegistrationRequest request,
        CurrentIdentityAccessor accessor,
        ProductIdentityService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            async () =>
            {
                var result = await service.RegisterSourceAsync(
                    accessor.Identity!,
                    request,
                    context.TraceIdentifier,
                    cancellationToken);
                return Results.Created(
                    $"/api/configuration/identity-sources/{result.IdentitySourceId}",
                    result);
            });

    private static Task<IResult> AllocateAsync(
        ProductIdentityAllocationRequest request,
        CurrentIdentityAccessor accessor,
        ProductIdentityService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            async () =>
            {
                var result = await service.AllocateAsync(
                    accessor.Identity!,
                    request,
                    context.TraceIdentifier,
                    cancellationToken);
                return result.IsReplay
                    ? Results.Ok(result)
                    : Results.Created($"/api/execution/product-identities/{result.ProductIdentityId}", result);
            });

    private static Task<IResult> StartWipAsync(
        Guid orderId,
        StartWipRequest request,
        CurrentIdentityAccessor accessor,
        ProductIdentityService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            async () =>
            {
                var result = await service.StartWipAsync(
                    accessor.Identity!,
                    orderId,
                    request,
                    context.TraceIdentifier,
                    cancellationToken);
                return result.IsReplay
                    ? Results.Ok(result)
                    : Results.Created(
                        $"/api/execution/product-identities/{result.ProductIdentityId}",
                        result);
            });

    private static Task<IResult> ReadWorkstationAsync(
        string serialNumber,
        CurrentIdentityAccessor accessor,
        ProductIdentityService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            async () => Results.Ok(await service.ReadWorkstationAsync(
                accessor.Identity!,
                serialNumber,
                context.TraceIdentifier,
                cancellationToken)));

    private static Task<IResult> UnbindAsync(
        Guid identityId,
        ProductIdentityCorrectionRequest request,
        CurrentIdentityAccessor accessor,
        ProductIdentityService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            async () => Results.Ok(await service.UnbindAsync(
                accessor.Identity!,
                identityId,
                request,
                context.TraceIdentifier,
                cancellationToken)));

    private static Task<IResult> VoidIdentityAsync(
        Guid identityId,
        ProductIdentityCorrectionRequest request,
        CurrentIdentityAccessor accessor,
        ProductIdentityService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            async () => Results.Ok(await service.VoidAsync(
                accessor.Identity!,
                identityId,
                request,
                context.TraceIdentifier,
                cancellationToken)));

    private static Task<IResult> PrintLabelAsync(
        Guid identityId,
        ProductLabelPrintRequest request,
        CurrentIdentityAccessor accessor,
        ProductIdentityService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            async () =>
            {
                var result = await service.PrintLabelAsync(
                    accessor.Identity!,
                    identityId,
                    request,
                    context.TraceIdentifier,
                    cancellationToken);
                return Results.Created(
                    $"/api/execution/product-identities/{identityId}/labels/{result.LabelId}",
                    result);
            });

    private static Task<IResult> ReprintLabelAsync(
        Guid identityId,
        Guid labelId,
        ProductLabelReasonRequest request,
        CurrentIdentityAccessor accessor,
        ProductIdentityService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            async () => Results.Ok(await service.ReprintLabelAsync(
                accessor.Identity!,
                identityId,
                labelId,
                request,
                context.TraceIdentifier,
                cancellationToken)));

    private static Task<IResult> VoidLabelAsync(
        Guid identityId,
        Guid labelId,
        ProductLabelReasonRequest request,
        CurrentIdentityAccessor accessor,
        ProductIdentityService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            async () => Results.Ok(await service.VoidLabelAsync(
                accessor.Identity!,
                identityId,
                labelId,
                request,
                context.TraceIdentifier,
                cancellationToken)));

    private static Task<IResult> ReplaceLabelAsync(
        Guid identityId,
        Guid labelId,
        ProductLabelReplaceRequest request,
        CurrentIdentityAccessor accessor,
        ProductIdentityService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            async () => Results.Ok(await service.ReplaceLabelAsync(
                accessor.Identity!,
                identityId,
                labelId,
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
        catch (ProductIdentityRejectedException exception)
        {
            return Results.Json(
                new { code = exception.Code, message = exception.Message },
                statusCode: exception.HttpStatusCode);
        }
    }
}
