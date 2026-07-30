using System.Text.Json;
using Mes.Api.Identity;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Integration;

namespace Mes.Api.Integration;

/// <summary>ERP 生产订单入站和 MES 订单工作台查询端点。</summary>
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
        JsonElement payload,
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
            var request = payload.Deserialize<ProductionOrderIngressRequest>(
                JsonSerializerOptions.Web);
            if (request is null)
            {
                return Results.BadRequest(new
                {
                    status = "Rejected",
                    code = "INBOUND_ENVELOPE_INVALID",
                    message = "生产订单入站报文必须是 JSON 对象；请由 ERP 集成负责人修正消息信封后重试。",
                });
            }

            var result = await ingress.ReceiveAsync(
                accessor.Identity,
                request,
                payload.GetRawText(),
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
        ProductionOrderWorkbenchQueryService workbench,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (accessor.Identity is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await workbench.ReadAsync(
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
