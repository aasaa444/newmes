using Mes.Api.Data;
using Mes.Api.MasterData;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Execution;

/// <summary>
/// 工单与领料领域服务：状态机、齐套计算、线边扣减。
/// 端点只做 HTTP/鉴权，规则集中在此便于测试与阅读。
/// </summary>
public class WorkOrderService(MesDbContext db)
{
    /// <summary>创建草稿工单；不冻结路线，可稍后下达。</summary>
    public async Task<WorkOrder> CreateDraftAsync(
        string? orderNo,
        Guid finishedMaterialId,
        decimal plannedQty,
        Guid? processRouteId,
        Guid? bomId,
        CancellationToken ct = default)
    {
        if (plannedQty <= 0)
        {
            throw new InvalidOperationException("plannedQty must be positive");
        }

        var fg = await db.Materials.FirstOrDefaultAsync(
            m => m.Id == finishedMaterialId && m.IsFinishedGood && m.IsActive, ct)
            ?? throw new InvalidOperationException("finished material not found or not a finished good");

        // 默认取该成品当前有效 BOM / 路线
        var bom = bomId.HasValue
            ? await db.Boms.Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == bomId && b.IsActive, ct)
            : await db.Boms.Include(b => b.Lines)
                .Where(b => b.FinishedMaterialId == fg.Id && b.IsActive)
                .OrderByDescending(b => b.Version)
                .FirstOrDefaultAsync(ct);

        var route = processRouteId.HasValue
            ? await db.ProcessRoutes.FirstOrDefaultAsync(r => r.Id == processRouteId && r.IsActive, ct)
            : await db.ProcessRoutes
                .Where(r => r.FinishedMaterialId == fg.Id && r.IsActive)
                .OrderByDescending(r => r.Version)
                .FirstOrDefaultAsync(ct);

        var no = string.IsNullOrWhiteSpace(orderNo)
            ? $"WO-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}"
            : orderNo.Trim().ToUpperInvariant();

        if (await db.WorkOrders.AnyAsync(w => w.OrderNo == no, ct))
        {
            throw new InvalidOperationException("order number already exists");
        }

        var wo = new WorkOrder
        {
            Id = Guid.NewGuid(),
            OrderNo = no,
            FinishedMaterialId = fg.Id,
            PlannedQty = plannedQty,
            Status = WorkOrderStatus.Draft,
            ProcessRouteId = route?.Id,
            BomId = bom?.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync(ct);
        return wo;
    }

    /// <summary>下达：冻结 BOM/路线版本；状态 Draft→Released。</summary>
    public async Task ReleaseAsync(Guid workOrderId, CancellationToken ct = default)
    {
        var wo = await LoadWoAsync(workOrderId, ct);
        if (wo.Status != WorkOrderStatus.Draft)
        {
            throw new InvalidOperationException("only draft work orders can be released");
        }

        var bom = wo.BomId.HasValue
            ? await db.Boms.FirstOrDefaultAsync(b => b.Id == wo.BomId, ct)
            : await db.Boms.Where(b => b.FinishedMaterialId == wo.FinishedMaterialId && b.IsActive)
                .OrderByDescending(b => b.Version).FirstOrDefaultAsync(ct);

        var route = wo.ProcessRouteId.HasValue
            ? await db.ProcessRoutes.FirstOrDefaultAsync(r => r.Id == wo.ProcessRouteId, ct)
            : await db.ProcessRoutes.Where(r => r.FinishedMaterialId == wo.FinishedMaterialId && r.IsActive)
                .OrderByDescending(r => r.Version).FirstOrDefaultAsync(ct);

        if (bom is null || route is null)
        {
            throw new InvalidOperationException("BOM and process route are required to release");
        }

        wo.BomId = bom.Id;
        wo.FrozenBomVersion = bom.Version;
        wo.ProcessRouteId = route.Id;
        wo.FrozenRouteVersion = route.Version;
        wo.Status = WorkOrderStatus.Released;
        wo.ReleasedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>取消：仅无在制 SN 且状态为 Draft 或 Released。</summary>
    public async Task CancelAsync(Guid workOrderId, CancellationToken ct = default)
    {
        var wo = await LoadWoAsync(workOrderId, ct);
        if (wo.Status is not (WorkOrderStatus.Draft or WorkOrderStatus.Released))
        {
            throw new InvalidOperationException("only draft or released orders can be cancelled");
        }

        if (wo.InProcessSerialCount > 0)
        {
            throw new InvalidOperationException("cannot cancel while in-process serials exist");
        }

        wo.Status = WorkOrderStatus.Cancelled;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 齐套：计划数 × BOM 单位用量 vs 线边可用；默认只提示不强制（返回 IsShort）。
    /// </summary>
    public async Task<KittingResult> GetKittingAsync(Guid workOrderId, CancellationToken ct = default)
    {
        var wo = await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workOrderId, ct)
            ?? throw new InvalidOperationException("work order not found");

        if (wo.BomId is null)
        {
            throw new InvalidOperationException("work order has no BOM; release first or assign BOM");
        }

        var bom = await db.Boms.AsNoTracking()
            .Include(b => b.Lines).ThenInclude(l => l.ComponentMaterial)
            .FirstAsync(b => b.Id == wo.BomId, ct);

        var lines = new List<KittingLineResult>();
        foreach (var bl in bom.Lines)
        {
            var required = bl.QuantityPer * wo.PlannedQty;
            var inv = await db.LineSideInventories.AsNoTracking()
                .FirstOrDefaultAsync(i => i.MaterialId == bl.ComponentMaterialId, ct);
            var onHand = inv?.QuantityOnHand ?? 0;
            var issued = await db.WorkOrderIssueLines.AsNoTracking()
                .Where(x => x.WorkOrderId == wo.Id && x.MaterialId == bl.ComponentMaterialId)
                .SumAsync(x => (decimal?)x.IssuedQty, ct) ?? 0;
            // 仍需从线边再领的量（简化：应领总额 - 已领）
            var stillNeed = Math.Max(0, required - issued);
            lines.Add(new KittingLineResult(
                bl.ComponentMaterial!.Code,
                bl.ComponentMaterial.Name,
                bl.ComponentMaterialId,
                bl.ComponentMaterial.IsKeyComponent,
                required,
                issued,
                onHand,
                stillNeed,
                onHand < stillNeed));
        }

        return new KittingResult(wo.Id, wo.OrderNo, lines, lines.Any(l => l.IsShort));
    }

    /// <summary>
    /// 按 BOM 应领差额从线边领满（或按请求行领指定数量）。
    /// 非关键件：Issued+Consumed；关键件：Issued+Pending。
    /// 线边不足时抛错（领料动作强制有料；齐套查询才是软提示）。
    /// </summary>
    public async Task<IReadOnlyList<WorkOrderIssueLine>> IssueMaterialsAsync(
        Guid workOrderId,
        IReadOnlyList<IssueMaterialRequestLine>? explicitLines,
        CancellationToken ct = default)
    {
        var wo = await LoadWoAsync(workOrderId, ct);
        if (wo.Status is not (WorkOrderStatus.Released or WorkOrderStatus.InProcess))
        {
            throw new InvalidOperationException("issue only allowed on released or in-process orders");
        }

        if (wo.BomId is null)
        {
            throw new InvalidOperationException("BOM required");
        }

        var bom = await db.Boms
            .Include(b => b.Lines).ThenInclude(l => l.ComponentMaterial)
            .FirstAsync(b => b.Id == wo.BomId, ct);

        // 默认：按齐套「仍需」数量领；也可显式传入行
        List<(Guid MaterialId, decimal Qty, Material Mat)> toIssue = [];
        if (explicitLines is { Count: > 0 })
        {
            foreach (var line in explicitLines)
            {
                if (line.Quantity <= 0) continue;
                var mat = await db.Materials.FirstOrDefaultAsync(m => m.Id == line.MaterialId, ct)
                    ?? throw new InvalidOperationException($"material {line.MaterialId} not found");
                toIssue.Add((mat.Id, line.Quantity, mat));
            }
        }
        else
        {
            var kit = await GetKittingAsync(workOrderId, ct);
            foreach (var k in kit.Lines.Where(l => l.StillNeedQty > 0))
            {
                var mat = bom.Lines.First(l => l.ComponentMaterialId == k.MaterialId).ComponentMaterial!;
                toIssue.Add((k.MaterialId, k.StillNeedQty, mat));
            }
        }

        if (toIssue.Count == 0)
        {
            return wo.IssueLines;
        }

        foreach (var (materialId, qty, mat) in toIssue)
        {
            var inv = await db.LineSideInventories.FirstOrDefaultAsync(i => i.MaterialId == materialId, ct);
            if (inv is null || inv.QuantityOnHand < qty)
            {
                throw new InvalidOperationException(
                    $"insufficient line-side stock for {mat.Code}: need {qty}, have {inv?.QuantityOnHand ?? 0}");
            }

            inv.QuantityOnHand -= qty;

            var issueLine = await db.WorkOrderIssueLines
                .FirstOrDefaultAsync(x => x.WorkOrderId == wo.Id && x.MaterialId == materialId, ct);
            if (issueLine is null)
            {
                issueLine = new WorkOrderIssueLine
                {
                    Id = Guid.NewGuid(),
                    WorkOrderId = wo.Id,
                    MaterialId = materialId
                };
                db.WorkOrderIssueLines.Add(issueLine);
            }

            issueLine.IssuedQty += qty;
            if (mat.IsKeyComponent)
            {
                // 关键件：待绑 SN
                issueLine.PendingQty += qty;
            }
            else
            {
                // 非关键件：领料即已耗
                issueLine.ConsumedQty += qty;
            }
        }

        await db.SaveChangesAsync(ct);
        return await db.WorkOrderIssueLines.Where(x => x.WorkOrderId == wo.Id).ToListAsync(ct);
    }

    /// <summary>线边收料（计划员补库存）。</summary>
    public async Task<LineSideInventory> ReceiveLineSideAsync(Guid materialId, decimal qty, CancellationToken ct = default)
    {
        if (qty <= 0) throw new InvalidOperationException("quantity must be positive");
        _ = await db.Materials.FirstOrDefaultAsync(m => m.Id == materialId, ct)
            ?? throw new InvalidOperationException("material not found");

        var inv = await db.LineSideInventories.FirstOrDefaultAsync(i => i.MaterialId == materialId, ct);
        if (inv is null)
        {
            inv = new LineSideInventory { Id = Guid.NewGuid(), MaterialId = materialId, QuantityOnHand = 0 };
            db.LineSideInventories.Add(inv);
        }

        inv.QuantityOnHand += qty;
        await db.SaveChangesAsync(ct);
        return inv;
    }

    private async Task<WorkOrder> LoadWoAsync(Guid id, CancellationToken ct) =>
        await db.WorkOrders.Include(w => w.IssueLines).FirstOrDefaultAsync(w => w.Id == id, ct)
        ?? throw new InvalidOperationException("work order not found");
}

public record IssueMaterialRequestLine(Guid MaterialId, decimal Quantity);

public record KittingLineResult(
    string MaterialCode,
    string MaterialName,
    Guid MaterialId,
    bool IsKeyComponent,
    decimal RequiredQty,
    decimal IssuedQty,
    decimal LineSideOnHand,
    decimal StillNeedQty,
    bool IsShort);

public record KittingResult(Guid WorkOrderId, string OrderNo, List<KittingLineResult> Lines, bool HasShortage);
