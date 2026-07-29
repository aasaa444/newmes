using Mes.Api.Identity;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Integration;

namespace Mes.Api.Integration;

public static class ProductionOrderEndpoints
{
    public static IEndpointRouteBuilder MapProductionOrderEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool includeSimulator)
    {
        endpoints.MapPost(
                "/api/integration/erp/production-orders",
                ReceiveAsync)
            .RequireAuthorization();
        if (includeSimulator)
        {
            endpoints.MapPost(
                    "/api/simulator/erp/production-orders",
                    ReceiveAsync)
                .RequireAuthorization();
        }

        endpoints.MapGet(
                "/api/planning/production-orders",
                ReadWorkbenchAsync)
            .RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> ReceiveAsync(
        ProductionOrderIngressRequest request,
        CurrentIdentityAccessor accessor,
        ProductionOrderIngressService ingress,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (accessor.Identity is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await ingress.ReceiveAsync(
                accessor.Identity,
                request,
                context.TraceIdentifier,
                cancellationToken);
            return Results.Json(result, statusCode: result.HttpStatusCode);
        }
        catch (CapabilityDeniedException exception)
        {
            return Forbidden(exception);
        }
    }

    private static async Task<IResult> ReadWorkbenchAsync(
        CurrentIdentityAccessor accessor,
        ProductionOrderIngressService ingress,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (accessor.Identity is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await ingress.ReadWorkbenchAsync(
                accessor.Identity,
                context.TraceIdentifier,
                cancellationToken));
        }
        catch (CapabilityDeniedException exception)
        {
            return Forbidden(exception);
        }
    }

    private static IResult Forbidden(CapabilityDeniedException exception) =>
        Results.Json(
            new { code = exception.ReasonCode, capability = exception.Capability.ToString() },
            statusCode: StatusCodes.Status403Forbidden);
}
