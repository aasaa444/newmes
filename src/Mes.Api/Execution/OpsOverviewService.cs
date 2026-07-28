using Mes.Api.Data;
using Mes.Api.MasterData;
using Microsoft.EntityFrameworkCore;

namespace Mes.Api.Execution;

/// <summary>
/// 经营总览（票 03）：用执行事实汇总今日五块 + 在制按工序分布。
/// 今日口径：服务端本地自然日（CONTEXT 今日口径）。
/// </summary>
public class OpsOverviewService(MesDbContext db)
{
    /// <summary>今日时间窗（[start, end)）。</summary>
    public static (DateTime start, DateTime end) TodayWindow(DateTime? now = null)
    {
        var today = (now ?? DateTime.Now).Date;
        return (today, today.AddDays(1));
    }

    /// <summary>汇总五块：WIP总数+工序分布、今日合格入库、今日报废、隔离待处理、工单进行中/未开工。</summary>
    public async Task<OpsOverviewDto> GetOverviewAsync(CancellationToken ct = default)
    {
        var (start, end) = TodayWindow();

        // ① 在制总数 + 按工序分布
        var wipRows = await db.ProductSerials.AsNoTracking()
            .Where(s => s.Status == ProcessStepStatus.InProcess)
            .Select(s => new { s.Id, s.CurrentProcessStepId })
            .ToListAsync(ct);

        var stepIds = wipRows.Where(r => r.CurrentProcessStepId.HasValue)
            .Select(r => r.CurrentProcessStepId!.Value).Distinct().ToList();
        var stepMap = stepIds.Count == 0
            ? new Dictionary<Guid, ProcessStep>()
            : await db.ProcessSteps.AsNoTracking()
                .Where(p => stepIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);

        var wipByProcess = wipRows
            .Where(r => r.CurrentProcessStepId.HasValue && stepMap.ContainsKey(r.CurrentProcessStepId!.Value))
            .GroupBy(r => stepMap[r.CurrentProcessStepId!.Value])
            .Select(g => new WipByProcessDto(
                g.Key.Id,
                g.Key.Code,
                g.Key.Name,
                g.Key.Sequence,
                g.Count()))
            .OrderBy(x => x.Sequence)
            .ToList();
        var wipTotal = wipRows.Count;

        // ② 今日合格入库：SerialPassRecords.Result == "Complete" 且 OccurredAt 在今日窗
        //    SQLite 不能在 SQL 里 ORDER BY/WHERE DateTimeOffset（与 audit 同款），先物化再内存过滤
        var allPass = await db.SerialPassRecords.AsNoTracking().ToListAsync(ct);
        var todayQualified = allPass.Count(p => p.Result == "Complete" && p.OccurredAt >= start && p.OccurredAt < end);
        var todayScraps = allPass.Count(p => p.Result == "Scrap" && p.OccurredAt >= start && p.OccurredAt < end);

        // ④ 隔离待处理
        var isolatedPending = await db.ProductSerials.AsNoTracking()
            .CountAsync(s => s.Status == ProcessStepStatus.Isolated, ct);

        // ⑤ 工单：进行中 + 已下达未开工
        var inProcessOrders = await db.WorkOrders.AsNoTracking()
            .CountAsync(w => w.Status == WorkOrderStatus.InProcess, ct);
        var releasedNotStarted = await db.WorkOrders.AsNoTracking()
            .CountAsync(w => w.Status == WorkOrderStatus.Released, ct);

        return new OpsOverviewDto(
            start,
            end,
            new WipBlock(wipTotal, wipByProcess),
            todayQualified,
            todayScraps,
            isolatedPending,
            new OrdersBlock(inProcessOrders, releasedNotStarted));
    }

    /// <summary>在制按工序 SN 列表（钻取）：可选按工序过滤。</summary>
    public async Task<List<WipSerialRow>> GetWipSerialsAsync(
        Guid? processStepId = null,
        CancellationToken ct = default)
    {
        var q = db.ProductSerials.AsNoTracking()
            .Include(s => s.WorkOrder)
            .Where(s => s.Status == ProcessStepStatus.InProcess);

        if (processStepId is Guid pid)
        {
            q = q.Where(s => s.CurrentProcessStepId == pid);
        }

        // SQLite 不能 ORDER BY DateTimeOffset；先物化再客户端排序
        var raw = await q.ToListAsync(ct);
        var rows = raw
            .OrderBy(s => s.CreatedAt)
            .Select(s => new WipSerialRow(
                s.Id,
                s.SerialNo,
                s.WorkOrder!.OrderNo,
                s.WorkOrderId,
                s.CurrentProcessStepId,
                s.CreatedAt))
            .ToList();

        // 在内存中补上工序 code（避免 EF 翻译子查询，兼容 SQLite）
        var stepIds = rows.Where(r => r.CurrentStepId.HasValue).Select(r => r.CurrentStepId!.Value).Distinct().ToList();
        if (stepIds.Count > 0)
        {
            var steps = await db.ProcessSteps.AsNoTracking()
                .Where(p => stepIds.Contains(p.Id))
                .ToListAsync(ct);
            var map = steps.ToDictionary(p => p.Id);
            return rows.Select(r =>
            {
                var code = r.CurrentStepId.HasValue && map.TryGetValue(r.CurrentStepId.Value, out var s) ? s.Code : null;
                return r with { CurrentStepCode = code };
            }).ToList();
        }

        return rows;
    }

    /// <summary>工单摘要列表（钻取）：inProcess | releasedNotStarted。</summary>
    public async Task<List<OrderSummaryRow>> GetOrderSummaryAsync(
        string bucket,
        CancellationToken ct = default)
    {
        var q = db.WorkOrders.AsNoTracking()
            .Include(w => w.FinishedMaterial)
            .AsQueryable();

        q = bucket switch
        {
            "inProcess" => q.Where(w => w.Status == WorkOrderStatus.InProcess),
            "releasedNotStarted" => q.Where(w => w.Status == WorkOrderStatus.Released),
            _ => q,
        };

        // SQLite 不能在 SQL 里 ORDER BY DateTimeOffset；先物化再客户端排序
        var rows = await q.ToListAsync(ct);
        return rows
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new OrderSummaryRow(
                w.Id, w.OrderNo,
                w.FinishedMaterial!.Code,
                w.PlannedQty, w.CompletedQty, w.ScrappedQty,
                w.Status.ToString(),
                w.CreatedAt, w.ReleasedAt))
            .ToList();
    }
}

public sealed record OpsOverviewDto(
    DateTime WindowStart,
    DateTime WindowEnd,
    WipBlock Wip,
    int TodayQualifiedReceipts,
    int TodayScraps,
    int IsolatedPendingCount,
    OrdersBlock Orders);

public sealed record WipBlock(int Total, List<WipByProcessDto> ByProcess);
public sealed record WipByProcessDto(Guid StepId, string StepCode, string StepName, int Sequence, int Count);
public sealed record OrdersBlock(int InProcess, int ReleasedNotStarted);

public sealed record WipSerialRow(
    Guid Id,
    string SerialNo,
    string WorkOrderNo,
    Guid WorkOrderId,
    Guid? CurrentStepId,
    DateTimeOffset CreatedAt)
{
    public string? CurrentStepCode { get; init; }
}

public sealed record OrderSummaryRow(
    Guid Id,
    string OrderNo,
    string FinishedMaterialCode,
    decimal PlannedQty,
    decimal CompletedQty,
    decimal ScrappedQty,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReleasedAt);
