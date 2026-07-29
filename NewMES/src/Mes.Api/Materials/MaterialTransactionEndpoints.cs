using Mes.Api.Identity;
using Mes.Domain.Materials;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Materials;

namespace Mes.Api.Materials;

public static class MaterialTransactionEndpoints
{
    public static IEndpointRouteBuilder MapMaterialTransactionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/material/line-side-transfers", TransferAsync)
            .RequireAuthorization();
        endpoints.MapPost("/api/material/order-issues", IssueAsync)
            .RequireAuthorization();
        endpoints.MapPost("/api/material/order-returns", ReturnAsync)
            .RequireAuthorization();
        endpoints.MapPost("/api/material/adjustments", AdjustAsync)
            .RequireAuthorization();
        endpoints.MapPost(
                "/api/material/transactions/{transactionId:guid}/reverse",
                ReverseAsync)
            .RequireAuthorization();
        endpoints.MapGet("/api/material/workbench", ReadWorkbenchAsync)
            .RequireAuthorization();
        return endpoints;
    }

    private static Task<IResult> TransferAsync(
        LineSideTransferRequest request,
        CurrentIdentityAccessor accessor,
        MaterialTransactionService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            () => service.TransferAsync(
                accessor.Identity!,
                request,
                context.TraceIdentifier,
                cancellationToken));

    private static Task<IResult> IssueAsync(
        OrderMaterialRequest request,
        CurrentIdentityAccessor accessor,
        MaterialTransactionService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            () => service.IssueAsync(
                accessor.Identity!,
                request,
                context.TraceIdentifier,
                cancellationToken));

    private static Task<IResult> ReturnAsync(
        OrderMaterialRequest request,
        CurrentIdentityAccessor accessor,
        MaterialTransactionService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            () => service.ReturnAsync(
                accessor.Identity!,
                request,
                context.TraceIdentifier,
                cancellationToken));

    private static Task<IResult> AdjustAsync(
        MaterialAdjustmentRequest request,
        CurrentIdentityAccessor accessor,
        MaterialTransactionService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            () => service.AdjustAsync(
                accessor.Identity!,
                request,
                context.TraceIdentifier,
                cancellationToken));

    private static Task<IResult> ReverseAsync(
        Guid transactionId,
        MaterialReversalRequest request,
        CurrentIdentityAccessor accessor,
        MaterialTransactionService service,
        HttpContext context,
        CancellationToken cancellationToken) => ExecuteAsync(
            accessor,
            () => service.ReverseAsync(
                accessor.Identity!,
                transactionId,
                request,
                context.TraceIdentifier,
                cancellationToken));

    private static async Task<IResult> ReadWorkbenchAsync(
        string? materialCode,
        string? lotNumber,
        Guid? productionOrderId,
        string? transactionType,
        CurrentIdentityAccessor accessor,
        MaterialWorkbenchQueryService service,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (accessor.Identity is null)
        {
            return Results.Unauthorized();
        }

        MaterialTransactionType? parsedType = null;
        if (transactionType is not null)
        {
            if (!Enum.TryParse<MaterialTransactionType>(
                    transactionType,
                    ignoreCase: true,
                    out var value)
                || !Enum.IsDefined(value))
            {
                return Results.Json(
                    new
                    {
                        code = "MATERIAL_TRANSACTION_TYPE_INVALID",
                        message = "事务类型无效；请使用工作台支持的物料事务类型。",
                    },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            parsedType = value;
        }

        try
        {
            return Results.Ok(await service.ReadAsync(
                accessor.Identity,
                new MaterialWorkbenchQuery(
                    materialCode,
                    lotNumber,
                    productionOrderId,
                    parsedType),
                context.TraceIdentifier,
                cancellationToken));
        }
        catch (CapabilityDeniedException exception)
        {
            return Forbidden(exception);
        }
    }

    private static async Task<IResult> ExecuteAsync(
        CurrentIdentityAccessor accessor,
        Func<Task<MaterialTransactionResult>> execute)
    {
        if (accessor.Identity is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await execute();
            return result.IsReplay
                ? Results.Ok(result)
                : Results.Created($"/api/material/transactions/{result.TransactionId}", result);
        }
        catch (CapabilityDeniedException exception)
        {
            return Forbidden(exception);
        }
        catch (MaterialTransactionRejectedException exception)
        {
            return Results.Json(
                new { code = exception.Code, message = exception.Message },
                statusCode: exception.HttpStatusCode);
        }
    }

    private static IResult Forbidden(CapabilityDeniedException exception) =>
        Results.Json(
            new { code = exception.ReasonCode, capability = exception.Capability.ToString() },
            statusCode: StatusCodes.Status403Forbidden);
}
