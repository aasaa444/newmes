using System.Security.Claims;
using Mes.Api.Data;
using Mes.Api.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Execution;

/// <summary>
/// 生产工单与线边库存 HTTP API（票 03）。
/// 写：计划员；列表/详情/齐套：业务角色只读（班组长看进度）。
/// </summary>
public static class WorkOrderEndpoints
{
    public static void MapWorkOrderEndpoints(this WebApplication app)
    {
        var read = app.MapGroup("/api").RequireAuthorization("AnyBusinessRole");
        var write = app.MapGroup("/api").RequireAuthorization("PlannerOnly");

        // ----- 线边库存 -----
        read.MapGet("/inventory/line-side", async (MesDbContext db) =>
        {
            var items = await db.LineSideInventories.AsNoTracking()
                .Include(i => i.Material)
                .OrderBy(i => i.Material!.Code)
                .Select(i => new LineSideInventoryResponse(
                    i.MaterialId, i.Material!.Code, i.Material.Name, i.QuantityOnHand))
                .ToListAsync();
            return Results.Ok(items);
        });

        write.MapPost("/inventory/line-side/receive", async (
            ReceiveLineSideRequest req, WorkOrderService svc, AuditService audit, ClaimsPrincipal user) =>
        {
            try
            {
                var inv = await svc.ReceiveLineSideAsync(req.MaterialId, req.Quantity);
                await audit.WriteAsync("LineSideReceived", user.Identity?.Name ?? "", "Material",
                    req.MaterialId.ToString(), $"qty={req.Quantity}");
                var mat = inv; // reload code via query would need db; return ids
                return Results.Ok(new { materialId = inv.MaterialId, quantityOnHand = inv.QuantityOnHand });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // ----- 工单 -----
        read.MapGet("/work-orders", async (MesDbContext db) =>
        {
            // 含用料行：列表可显示是否已领料（IssueLines 有数量即已领过）
            // 先物化再按 CreatedAt 排序：SQLite 不能 ORDER BY DateTimeOffset
            var rows = await db.WorkOrders.AsNoTracking()
                .Include(w => w.FinishedMaterial)
                .Include(w => w.IssueLines)
                .ToListAsync();
            return Results.Ok(rows.OrderByDescending(w => w.CreatedAt).Select(ToSummary).ToList());
        });

        read.MapGet("/work-orders/{id:guid}", async (Guid id, MesDbContext db) =>
        {
            var w = await db.WorkOrders.AsNoTracking()
                .Include(x => x.FinishedMaterial)
                .Include(x => x.IssueLines).ThenInclude(l => l.Material)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (w is null) return Results.NotFound();
            return Results.Ok(ToDetail(w));
        });

        write.MapPost("/work-orders", async (
            CreateWorkOrderRequest req, WorkOrderService svc, AuditService audit, ClaimsPrincipal user, MesDbContext db) =>
        {
            try
            {
                var wo = await svc.CreateDraftAsync(
                    req.OrderNo, req.FinishedMaterialId, req.PlannedQty, req.ProcessRouteId, req.BomId);
                await audit.WriteAsync("WorkOrderCreated", user.Identity?.Name ?? "", "WorkOrder", wo.Id.ToString(), wo.OrderNo);
                var loaded = await db.WorkOrders.AsNoTracking()
                    .Include(x => x.FinishedMaterial)
                    .Include(x => x.IssueLines)
                    .FirstAsync(x => x.Id == wo.Id);
                return Results.Created($"/api/work-orders/{wo.Id}", ToSummary(loaded));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        write.MapPost("/work-orders/{id:guid}/release", async (
            Guid id, WorkOrderService svc, AuditService audit, ClaimsPrincipal user, MesDbContext db) =>
        {
            try
            {
                await svc.ReleaseAsync(id);
                await audit.WriteAsync("WorkOrderReleased", user.Identity?.Name ?? "", "WorkOrder", id.ToString(), null);
                var w = await db.WorkOrders.AsNoTracking()
                    .Include(x => x.FinishedMaterial)
                    .Include(x => x.IssueLines)
                    .FirstAsync(x => x.Id == id);
                return Results.Ok(ToSummary(w));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        write.MapPost("/work-orders/{id:guid}/cancel", async (
            Guid id, WorkOrderService svc, AuditService audit, ClaimsPrincipal user, MesDbContext db) =>
        {
            try
            {
                await svc.CancelAsync(id);
                await audit.WriteAsync("WorkOrderCancelled", user.Identity?.Name ?? "", "WorkOrder", id.ToString(), null);
                var w = await db.WorkOrders.AsNoTracking()
                    .Include(x => x.FinishedMaterial)
                    .Include(x => x.IssueLines)
                    .FirstAsync(x => x.Id == id);
                return Results.Ok(ToSummary(w));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        read.MapGet("/work-orders/{id:guid}/kitting", async (Guid id, WorkOrderService svc) =>
        {
            try
            {
                var kit = await svc.GetKittingAsync(id);
                return Results.Ok(kit);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        write.MapPost("/work-orders/{id:guid}/issue", async (
            Guid id, IssueMaterialsRequest? req, WorkOrderService svc, AuditService audit, ClaimsPrincipal user, MesDbContext db) =>
        {
            try
            {
                var lines = req?.Lines?
                    .Select(l => new IssueMaterialRequestLine(l.MaterialId, l.Quantity))
                    .ToList();
                await svc.IssueMaterialsAsync(id, lines);
                await audit.WriteAsync("WorkOrderIssued", user.Identity?.Name ?? "", "WorkOrder", id.ToString(), null);
                var w = await db.WorkOrders.AsNoTracking()
                    .Include(x => x.FinishedMaterial)
                    .Include(x => x.IssueLines).ThenInclude(l => l.Material)
                    .FirstAsync(x => x.Id == id);
                return Results.Ok(ToDetail(w));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static WorkOrderSummaryResponse ToSummary(WorkOrder w)
    {
        // 任一用料行 IssuedQty>0 即视为已领过料（可再次补领，但 UI 会标「已领料」）
        var issuedLines = w.IssueLines?.Count(l => l.IssuedQty > 0) ?? 0;
        var materialIssued = issuedLines > 0;
        return new(
            w.Id,
            w.OrderNo,
            w.FinishedMaterial?.Code ?? "",
            w.PlannedQty,
            w.CompletedQty,
            w.ScrappedQty,
            w.Status.ToString(),
            w.FrozenRouteVersion,
            w.FrozenBomVersion,
            w.InProcessSerialCount,
            materialIssued,
            issuedLines,
            w.CreatedAt,
            w.ReleasedAt);
    }

    private static WorkOrderDetailResponse ToDetail(WorkOrder w) => new(
        ToSummary(w),
        w.IssueLines.Select(l => new IssueLineResponse(
            l.MaterialId,
            l.Material?.Code ?? "",
            l.IssuedQty,
            l.PendingQty,
            l.ConsumedQty,
            l.Material?.IsKeyComponent ?? false)).ToList());
}

public record CreateWorkOrderRequest(
    string? OrderNo,
    Guid FinishedMaterialId,
    decimal PlannedQty,
    Guid? ProcessRouteId,
    Guid? BomId);

public record IssueLineRequestDto(Guid MaterialId, decimal Quantity);
public record IssueMaterialsRequest(List<IssueLineRequestDto>? Lines);

public record ReceiveLineSideRequest(Guid MaterialId, decimal Quantity);

public record LineSideInventoryResponse(Guid MaterialId, string MaterialCode, string MaterialName, decimal QuantityOnHand);

public record WorkOrderSummaryResponse(
    Guid Id,
    string OrderNo,
    string FinishedMaterialCode,
    decimal PlannedQty,
    decimal CompletedQty,
    decimal ScrappedQty,
    string Status,
    string? FrozenRouteVersion,
    string? FrozenBomVersion,
    int InProcessSerialCount,
    /// <summary>是否已发生过领料（用料台账有 IssuedQty&gt;0）。</summary>
    bool MaterialIssued,
    /// <summary>已领料物料行数。</summary>
    int IssuedLineCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReleasedAt);

public record IssueLineResponse(
    Guid MaterialId,
    string MaterialCode,
    decimal IssuedQty,
    decimal PendingQty,
    decimal ConsumedQty,
    bool IsKeyComponent);

public record WorkOrderDetailResponse(WorkOrderSummaryResponse Header, List<IssueLineResponse> IssueLines);
