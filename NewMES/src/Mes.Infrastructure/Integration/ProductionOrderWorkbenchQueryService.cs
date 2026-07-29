using Mes.Domain.Identity;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Integration;

public sealed class ProductionOrderWorkbenchQueryService(
    MesDbContext context,
    IdentityAccessService identityAccess)
{
    public async Task<ProductionOrderWorkbenchResult> ReadAsync(
        EffectiveIdentity actor,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.ProductionOrderRead,
            "PRODUCTION_ORDER_WORKBENCH_READ",
            "ProductionOrder",
            actor.UserId.ToString(),
            correlationId,
            cancellationToken);

        var orders = await context.ProductionOrders
            .AsNoTracking()
            .OrderByDescending(order => order.CreatedAtUtc)
            .Select(order => new
            {
                Order = order,
                LatestInbox = context.IntegrationInboxMessages
                    .Where(message => message.ProductionOrderId == order.Id)
                    .OrderByDescending(message => message.ProcessedAtUtc)
                    .ThenByDescending(message => message.Id)
                    .Select(message => new
                    {
                        Status = message.Status.ToString(),
                        message.ResultCode,
                    })
                    .FirstOrDefault(),
            })
            .Select(item => new ProductionOrderWorkbenchItem(
                item.Order.Id,
                item.Order.OrderNumber,
                item.Order.Material!.Code,
                item.Order.PlannedQuantity,
                item.Order.Status.ToString(),
                item.Order.SourceSystem,
                item.Order.SourceReference,
                item.Order.SourceVersion,
                item.LatestInbox == null ? null : item.LatestInbox.Status,
                item.LatestInbox == null ? null : item.LatestInbox.ResultCode))
            .ToArrayAsync(cancellationToken);

        var inboundResults = await context.IntegrationInboxMessages
            .AsNoTracking()
            .OrderByDescending(message => message.ProcessedAtUtc)
            .ThenByDescending(message => message.Id)
            .Select(message => new ProductionOrderInboundResultItem(
                message.Id,
                message.MessageType,
                message.SourceSystem,
                message.MessageId,
                message.BusinessKey,
                message.SourceVersion,
                message.ContractVersion,
                message.ProductionOrderId,
                message.Status.ToString(),
                message.ResultCode,
                message.ResultMessage,
                message.ProcessedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new ProductionOrderWorkbenchResult(orders, inboundResults);
    }
}
