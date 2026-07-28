using Mes.Api.Data;
using Mes.Api.Integration;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Execution;

/// <summary>
/// 完工入库与工单关闭（票 06）。
/// 仅 RouteCompleted 且非隔离/报废可入库；更新成品账、在制计数、工单完工数；触发 ERP 回写模拟。
/// </summary>
public class CompletionService(MesDbContext db, ErpWritebackSimulator erp)
{
    /// <summary>合格 SN 完工入库。</summary>
    public async Task<ProductSerial> CompleteToFinishedGoodsAsync(
        string serialNo,
        string operatorUserName,
        CancellationToken ct = default)
    {
        serialNo = serialNo.Trim().ToUpperInvariant();
        var serial = await db.ProductSerials
            .Include(s => s.WorkOrder)!.ThenInclude(w => w!.FinishedMaterial)
            .FirstOrDefaultAsync(s => s.SerialNo == serialNo, ct)
            ?? throw new InvalidOperationException("未找到该成品序列号");

        if (serial.Status == ProcessStepStatus.Isolated)
        {
            throw new InvalidOperationException("隔离中的序列号不能入成品仓，请先放行或报废");
        }

        if (serial.Status == ProcessStepStatus.Scrapped)
        {
            throw new InvalidOperationException("已报废的序列号不能入成品仓");
        }

        if (serial.Status == ProcessStepStatus.Completed)
        {
            throw new InvalidOperationException("该序列号已完工入库");
        }

        if (serial.Status != ProcessStepStatus.RouteCompleted)
        {
            throw new InvalidOperationException(
                $"只有路线完成的序列号才能完工入库，当前状态为 {serial.Status}");
        }

        var wo = await db.WorkOrders
            .Include(w => w.FinishedMaterial)
            .FirstAsync(w => w.Id == serial.WorkOrderId, ct);

        if (wo.Status is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled)
        {
            throw new InvalidOperationException("生产工单已关闭或已取消");
        }

        // 成品账 +1
        var fgId = wo.FinishedMaterialId;
        var inv = await db.FinishedGoodsInventories.FirstOrDefaultAsync(i => i.MaterialId == fgId, ct);
        if (inv is null)
        {
            inv = new FinishedGoodsInventory
            {
                Id = Guid.NewGuid(),
                MaterialId = fgId,
                QuantityOnHand = 0
            };
            db.FinishedGoodsInventories.Add(inv);
        }

        inv.QuantityOnHand += 1;

        serial.Status = ProcessStepStatus.Completed;
        serial.CurrentProcessStepId = null;

        if (wo.InProcessSerialCount > 0)
        {
            wo.InProcessSerialCount -= 1;
        }

        wo.CompletedQty += 1;

        // 计划达成（含报废）→ 工单已完工
        if (wo.CompletedQty + wo.ScrappedQty >= wo.PlannedQty
            && wo.Status is WorkOrderStatus.InProcess or WorkOrderStatus.Released)
        {
            wo.Status = WorkOrderStatus.Completed;
        }

        // 谱系：入库事件（客户端排序，兼容 SQLite DateTimeOffset）
        var passRows = await db.SerialPassRecords.AsNoTracking()
            .Where(p => p.ProductSerialId == serial.Id)
            .ToListAsync(ct);
        var lastStepId = passRows.OrderByDescending(p => p.OccurredAt).Select(p => p.ProcessStepId).FirstOrDefault();

        if (lastStepId == Guid.Empty && wo.ProcessRouteId is Guid rid)
        {
            lastStepId = await db.ProcessSteps.AsNoTracking()
                .Where(p => p.ProcessRouteId == rid)
                .OrderByDescending(p => p.Sequence)
                .Select(p => p.Id)
                .FirstAsync(ct);
        }

        if (lastStepId != Guid.Empty)
        {
            db.SerialPassRecords.Add(new SerialPassRecord
            {
                Id = Guid.NewGuid(),
                ProductSerialId = serial.Id,
                ProcessStepId = lastStepId,
                Result = "Complete",
                OperatorUserName = operatorUserName,
                OccurredAt = DateTimeOffset.UtcNow,
                Remark = "Finished goods receipt"
            });
        }

        await db.SaveChangesAsync(ct);

        await erp.EnqueueProductionReceiptAsync(
            wo,
            serial.SerialNo,
            wo.FinishedMaterial?.Code ?? "",
            1,
            ct);

        return serial;
    }

    /// <summary>
    /// 关闭工单：无在制 SN，且已完工或计划已由完工+报废覆盖。
    /// 关闭后关键写操作只读。
    /// </summary>
    public async Task CloseAsync(Guid workOrderId, CancellationToken ct = default)
    {
        var wo = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == workOrderId, ct)
            ?? throw new InvalidOperationException("未找到该生产工单");

        if (wo.Status == WorkOrderStatus.Closed)
        {
            throw new InvalidOperationException("工单已关闭");
        }

        if (wo.Status == WorkOrderStatus.Cancelled)
        {
            throw new InvalidOperationException("已取消的工单无需再关闭");
        }

        if (wo.InProcessSerialCount > 0)
        {
            throw new InvalidOperationException("仍有在制序列号，不能关闭工单");
        }

        // 允许：已完工，或 Released/InProcess 但已无在制且完工+报废已覆盖计划
        var covered = wo.CompletedQty + wo.ScrappedQty >= wo.PlannedQty;
        if (wo.Status != WorkOrderStatus.Completed && !covered)
        {
            throw new InvalidOperationException(
                "关闭工单要求：已完工，或合格完工数+报废数已覆盖计划数量");
        }

        wo.Status = WorkOrderStatus.Closed;
        await db.SaveChangesAsync(ct);
        await erp.EnqueueWorkOrderCloseAsync(wo, ct);
    }
}
