using Mes.Domain.Identity;
using Mes.Infrastructure.Execution;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Integration;

/// <summary>为订单工作台组合业务订单与入站处理证据；查询模型不参与订单状态变更。</summary>
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

        var orderRows = await context.ProductionOrders
            .AsNoTracking()
            .OrderByDescending(order => order.CreatedAtUtc)
            .Select(order => new
            {
                order.Id,
                order.OrderNumber,
                MaterialCode = order.Material!.Code,
                order.PlannedQuantity,
                order.StartedQuantity,
                order.QualifiedQuantity,
                order.ScrappedQuantity,
                order.OpenQualityHoldQuantity,
                order.Status,
                order.WarehouseHandoffCompleted,
                order.ErpReconciled,
                order.SourceSystem,
                order.SourceReference,
                order.SourceVersion,
                SnapshotVersion = context.ProductionOrderExecutionSnapshots
                    .Where(snapshot => snapshot.ProductionOrderId == order.Id)
                    .Select(snapshot => snapshot.SnapshotVersion)
                    .SingleOrDefault(),
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
            .ToArrayAsync(cancellationToken);
        var orders = orderRows.Select(item => new ProductionOrderWorkbenchItem(
                item.Id,
                item.OrderNumber,
                item.MaterialCode,
                item.PlannedQuantity,
                item.StartedQuantity,
                item.QualifiedQuantity,
                item.ScrappedQuantity,
                item.PlannedQuantity - item.StartedQuantity,
                item.StartedQuantity - item.QualifiedQuantity - item.ScrappedQuantity,
                item.Status.ToString(),
                item.SnapshotVersion,
                ProductionOrderCommandPolicy.AvailableCommands(
                    item.Status,
                    !string.IsNullOrWhiteSpace(item.SourceSystem)
                        && !string.IsNullOrWhiteSpace(item.SourceReference)
                        && !string.IsNullOrWhiteSpace(item.SourceVersion),
                    item.StartedQuantity,
                    item.QualifiedQuantity,
                    item.ScrappedQuantity,
                    item.OpenQualityHoldQuantity,
                    item.WarehouseHandoffCompleted,
                    item.ErpReconciled),
                item.SourceSystem,
                item.SourceReference,
                item.SourceVersion,
                item.LatestInbox == null ? null : item.LatestInbox.Status,
                item.LatestInbox == null ? null : item.LatestInbox.ResultCode))
            .ToArray();

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
