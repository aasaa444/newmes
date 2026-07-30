using Mes.Domain.Identity;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Materials;

/// <summary>从只追加物料账汇总线边量、订单量和交易明细，供工作台核对而非直接修正余额。</summary>
public sealed class MaterialWorkbenchQueryService(
    MesDbContext context,
    IdentityAccessService identityAccess)
{
    public async Task<MaterialWorkbenchResult> ReadAsync(
        EffectiveIdentity actor,
        MaterialWorkbenchQuery query,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.MaterialTransactionExecute,
            "MATERIAL_WORKBENCH_READ",
            "MaterialTransaction",
            "workbench",
            correlationId,
            cancellationToken);

        var materialCode = NormalizeNullable(query.MaterialCode);
        var lotNumber = NormalizeNullable(query.LotNumber);
        var baseQuery = context.MaterialTransactions.AsNoTracking();
        if (materialCode is not null)
        {
            baseQuery = baseQuery.Where(item => item.Material!.Code == materialCode);
        }

        if (lotNumber is not null)
        {
            baseQuery = baseQuery.Where(item => item.LotNumber == lotNumber);
        }

        var lineSideRows = await baseQuery
            .GroupBy(item => new
            {
                item.MaterialId,
                MaterialCode = item.Material!.Code,
                item.LotNumber,
                item.Unit,
            })
            .Select(group => new
            {
                group.Key.MaterialId,
                group.Key.MaterialCode,
                group.Key.LotNumber,
                group.Key.Unit,
                Quantity = group.Sum(item => item.LineSideQuantityDelta),
            })
            .OrderBy(item => item.MaterialCode)
            .ThenBy(item => item.LotNumber)
            .ToArrayAsync(cancellationToken);
        var lineSideBalances = lineSideRows
            .Select(item => new MaterialLineSideBalance(
                item.MaterialId,
                item.MaterialCode,
                item.LotNumber,
                item.Unit,
                item.Quantity))
            .ToArray();

        var orderQuery = baseQuery.Where(item => item.ProductionOrderId != null);
        if (query.ProductionOrderId is not null)
        {
            orderQuery = orderQuery.Where(item => item.ProductionOrderId == query.ProductionOrderId);
        }

        var orderRows = await orderQuery
            .GroupBy(item => new
            {
                ProductionOrderId = item.ProductionOrderId!.Value,
                OrderNumber = item.ProductionOrder!.OrderNumber,
                item.MaterialId,
                MaterialCode = item.Material!.Code,
                item.LotNumber,
                item.Unit,
            })
            .Select(group => new
            {
                group.Key.ProductionOrderId,
                group.Key.OrderNumber,
                group.Key.MaterialId,
                group.Key.MaterialCode,
                group.Key.LotNumber,
                group.Key.Unit,
                AvailableQuantity = group.Sum(item => item.OrderAvailableQuantityDelta),
                NetIssuedQuantity = group.Sum(item => item.OrderIssuedQuantityDelta),
            })
            .OrderBy(item => item.OrderNumber)
            .ThenBy(item => item.MaterialCode)
            .ThenBy(item => item.LotNumber)
            .ToArrayAsync(cancellationToken);
        var orderBalances = orderRows
            .Select(item => new MaterialOrderBalance(
                item.ProductionOrderId,
                item.OrderNumber,
                item.MaterialId,
                item.MaterialCode,
                item.LotNumber,
                item.Unit,
                item.AvailableQuantity,
                item.NetIssuedQuantity))
            .ToArray();

        var historyQuery = baseQuery;
        if (query.ProductionOrderId is not null)
        {
            historyQuery = historyQuery.Where(item => item.ProductionOrderId == query.ProductionOrderId);
        }

        if (query.TransactionType is not null)
        {
            historyQuery = historyQuery.Where(item => item.TransactionType == query.TransactionType);
        }

        var transactionRows = await historyQuery
            .OrderByDescending(item => item.RecordedAtUtc)
            .ThenByDescending(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.TransactionType,
                MaterialCode = item.Material!.Code,
                item.LotNumber,
                item.Quantity,
                item.Unit,
                item.LineSideQuantityDelta,
                item.OrderAvailableQuantityDelta,
                item.OrderIssuedQuantityDelta,
                item.ProductionOrderId,
                OrderNumber = item.ProductionOrder == null
                    ? null
                    : item.ProductionOrder.OrderNumber,
                item.SourceSystem,
                item.IdempotencyKey,
                item.SourceDocumentType,
                item.SourceDocumentNumber,
                item.FromParty,
                item.ToParty,
                item.ReversesTransactionId,
                item.Reason,
                item.OccurredAtUtc,
                item.RecordedAtUtc,
            })
            .ToArrayAsync(cancellationToken);
        var transactions = transactionRows
            .Select(item => new MaterialTransactionView(
                item.Id,
                item.TransactionType.ToString(),
                item.MaterialCode,
                item.LotNumber,
                item.Quantity,
                item.Unit,
                item.LineSideQuantityDelta,
                item.OrderAvailableQuantityDelta,
                item.OrderIssuedQuantityDelta,
                item.ProductionOrderId,
                item.OrderNumber,
                item.SourceSystem,
                item.IdempotencyKey,
                item.SourceDocumentType,
                item.SourceDocumentNumber,
                item.FromParty,
                item.ToParty,
                item.ReversesTransactionId,
                item.Reason,
                item.OccurredAtUtc,
                item.RecordedAtUtc))
            .ToArray();

        return new MaterialWorkbenchResult(lineSideBalances, orderBalances, transactions);
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
