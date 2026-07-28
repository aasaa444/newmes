using System.Security.Claims;
using Mes.Api.Data;
using Mes.Api.Identity;
using Mes.Api.Integration;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Execution;

/// <summary>
/// 完工入库、成品库存、工单关闭、ERP 出站查询（票 06）。
/// </summary>
public static class CompletionEndpoints
{
    public static void MapCompletionEndpoints(this WebApplication app)
    {
        var read = app.MapGroup("/api").RequireAuthorization("AnyBusinessRole");
        var write = app.MapGroup("/api").RequireAuthorization("PlannerOnly");

        read.MapGet("/inventory/finished-goods", async (MesDbContext db) =>
        {
            var items = await db.FinishedGoodsInventories.AsNoTracking()
                .Include(i => i.Material)
                .OrderBy(i => i.Material!.Code)
                .Select(i => new FinishedGoodsInventoryResponse(
                    i.MaterialId, i.Material!.Code, i.Material.Name, i.QuantityOnHand))
                .ToListAsync();
            return Results.Ok(items);
        });

        // 过站角色也可入库（产线末端）；计划员同样可以
        var station = app.MapGroup("/api").RequireAuthorization("StationRoles");
        station.MapPost("/completion/receive", async (
            CompleteSerialRequest req,
            CompletionService completion,
            AuditService audit,
            ClaimsPrincipal user,
            MesDbContext db) =>
        {
            try
            {
                var op = user.Identity?.Name ?? "";
                var serial = await completion.CompleteToFinishedGoodsAsync(req.SerialNo, op);
                await audit.WriteAsync("FinishedGoodsReceived", op, "ProductSerial", serial.Id.ToString(), serial.SerialNo);

                var cur = serial.CurrentProcessStepId is null
                    ? null
                    : await db.ProcessSteps.AsNoTracking().FirstOrDefaultAsync(p => p.Id == serial.CurrentProcessStepId);

                return Results.Ok(new SerialStatusResponse(
                    serial.Id,
                    serial.SerialNo,
                    serial.WorkOrderId,
                    serial.Status.ToString(),
                    cur?.Code,
                    cur?.Name));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        write.MapPost("/work-orders/{id:guid}/close", async (
            Guid id,
            CompletionService completion,
            AuditService audit,
            ClaimsPrincipal user,
            MesDbContext db) =>
        {
            try
            {
                await completion.CloseAsync(id);
                await audit.WriteAsync("WorkOrderClosed", user.Identity?.Name ?? "", "WorkOrder", id.ToString(), null);
                var w = await db.WorkOrders.AsNoTracking()
                    .Include(x => x.FinishedMaterial)
                    .FirstAsync(x => x.Id == id);
                return Results.Ok(new
                {
                    w.Id,
                    w.OrderNo,
                    status = w.Status.ToString(),
                    w.CompletedQty,
                    w.ScrappedQty,
                    w.PlannedQty
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        read.MapGet("/erp/outbox", async (ErpWritebackSimulator erp) =>
        {
            var items = await erp.ListRecentAsync(100);
            return Results.Ok(items.Select(m => new ErpOutboxResponse(
                m.Id, m.MessageType, m.BusinessKey, m.PayloadJson, m.CreatedAt, m.Status)));
        });
    }
}

public record CompleteSerialRequest(string SerialNo);
public record FinishedGoodsInventoryResponse(Guid MaterialId, string MaterialCode, string MaterialName, decimal QuantityOnHand);
public record ErpOutboxResponse(
    Guid Id,
    string MessageType,
    string? BusinessKey,
    string PayloadJson,
    DateTimeOffset CreatedAt,
    string Status);
